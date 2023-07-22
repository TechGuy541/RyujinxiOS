package org.ryujinx.android

import android.content.Context
import android.os.Build
import android.view.SurfaceHolder
import android.view.SurfaceView
import org.ryujinx.android.viewmodels.GameModel
import org.ryujinx.android.viewmodels.MainViewModel
import org.ryujinx.android.viewmodels.QuickSettings
import org.ryujinx.android.viewmodels.VulkanDriverViewModel
import java.io.File
import kotlin.concurrent.thread

class GameHost(context: Context?, val controller: GameController, val mainViewModel: MainViewModel) : SurfaceView(context), SurfaceHolder.Callback {
    private var _renderingThreadWatcher: Thread? = null
    private var _height: Int = 0
    private var _width: Int = 0
    private var _updateThread: Thread? = null
    private var nativeInterop: NativeGraphicsInterop? = null
    private var _guestThread: Thread? = null
    private var _isInit: Boolean = false
    private var _isStarted: Boolean = false
    private var _nativeWindow: Long = 0
    private var _nativeHelper: NativeHelpers = NativeHelpers()

    private var _nativeRyujinx: RyujinxNative = RyujinxNative()

    companion object {
        var gameModel: GameModel? = null
    }

    init {
        holder.addCallback(this)
    }

    override fun surfaceCreated(holder: SurfaceHolder) {
    }

    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
        val isStarted = _isStarted

        start(holder)

        if(isStarted && (_width != width || _height != height))
        {
            val nativeHelpers = NativeHelpers()
            val window = nativeHelpers.getNativeWindow(holder.surface)
            _nativeRyujinx.graphicsSetSurface(window)
        }

        _width = width
        _height = height

        if(_isStarted)
        {
            _nativeRyujinx.inputSetClientSize(width, height)
        }
    }

    override fun surfaceDestroyed(holder: SurfaceHolder) {

    }

    private fun start(surfaceHolder: SurfaceHolder) {
        val game = gameModel ?: return
        val path = game.getPath() ?: return
        if (_isStarted)
            return

        var surface = surfaceHolder.surface

        val settings = QuickSettings(mainViewModel.activity)

        var success = _nativeRyujinx.graphicsInitialize(GraphicsConfiguration().apply {
            EnableShaderCache = settings.enableShaderCache
            EnableTextureRecompression = settings.enableTextureRecompression
            ResScale = settings.resScale
        })


        val nativeHelpers = NativeHelpers()
        val window = nativeHelpers.getNativeWindow(surfaceHolder.surface)
        nativeInterop = NativeGraphicsInterop()
        nativeInterop!!.VkRequiredExtensions = arrayOf(
            "VK_KHR_surface", "VK_KHR_android_surface"
        )
        nativeInterop!!.VkCreateSurface = nativeHelpers.getCreateSurfacePtr()
        nativeInterop!!.SurfaceHandle = window

        var driverViewModel = VulkanDriverViewModel(mainViewModel.activity);
        driverViewModel.getAvailableDrivers()

        var driverHandle = 0L;

        if(driverViewModel.selected.isNotEmpty()) {
            var privatePath = mainViewModel.activity.filesDir;
            var privateDriverPath = privatePath.absolutePath + "/driver/"
            val pD = File(privateDriverPath)
            if(pD.exists())
                pD.deleteRecursively()

            pD.mkdirs()

            var driver = File(driverViewModel.selected)
            var parent = driver.parentFile
            for (file in parent.walkTopDown()){
                if(file.absolutePath == parent.absolutePath)
                    continue
                file.copyTo(File(privateDriverPath + file.name), true)
            }
            driverHandle = NativeHelpers().loadDriver(mainViewModel.activity.applicationInfo.nativeLibraryDir!! + "/", privateDriverPath, driver.name)
        }

        success = _nativeRyujinx.graphicsInitializeRenderer(
            nativeInterop!!.VkRequiredExtensions!!,
            window,
            driverHandle
        )


        success = _nativeRyujinx.deviceInitialize(
            settings.isHostMapped,
            settings.useNce,
            SystemLanguage.AmericanEnglish.ordinal,
            RegionCode.USA.ordinal,
            settings.enableVsync,
            settings.enableDocked,
            settings.enablePtc,
            false,
            "UTC",
            settings.ignoreMissingServices
        )

        success = _nativeRyujinx.deviceLoad(path)

        _nativeRyujinx.inputInitialize(width, height)

        if(!settings.useVirtualController){
            controller.setVisible(false)
        }
        else{
            controller.connect()
        }

        mainViewModel.activity.physicalControllerManager.connect()

        //

        _nativeRyujinx.graphicsRendererSetSize(
            surfaceHolder.surfaceFrame.width(),
            surfaceHolder.surfaceFrame.height()
        )

        _guestThread = thread(start = true) {
            runGame()
        }
        _isStarted = success

        _updateThread = thread(start = true) {
            var c = 0
            while (_isStarted) {
                _nativeRyujinx.inputUpdate()
                Thread.sleep(1)
                c++
                if (c >= 1000) {
                    c = 0
                    mainViewModel.updateStats(_nativeRyujinx.deviceGetGameFifo(), _nativeRyujinx.deviceGetGameFrameRate(), _nativeRyujinx.deviceGetGameFrameTime())
                }
            }
        }
    }

    private fun runGame() {
        // RenderingThreadWatcher
        _renderingThreadWatcher = thread(start = true) {
            var threadId = 0L
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                mainViewModel.performanceManager?.enable()
                while (_isStarted) {
                    Thread.sleep(1000)
                    val newthreadId = mainViewModel.activity.getRenderingThreadId()

                    if (threadId != newthreadId) {
                        mainViewModel.performanceManager?.closeCurrentRenderingSession()
                    }
                    threadId = newthreadId
                    if (threadId != 0L) {
                        mainViewModel.performanceManager?.initializeRenderingSession(threadId)
                    }
                }
            }
        }
        _nativeRyujinx.graphicsRendererRunLoop()
    }
}