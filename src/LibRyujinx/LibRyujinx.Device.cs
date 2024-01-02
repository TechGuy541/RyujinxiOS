using ARMeilleure.Translation;
using LibHac.Ncm;
using LibHac.Tools.FsSystem.NcaUtils;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.FileSystem;
using Ryujinx.HLE.HOS.SystemState;
using Ryujinx.Input.HLE;
using Silk.NET.Vulkan;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LibRyujinx
{
    public static partial class LibRyujinx
    {
        [UnmanagedCallersOnly(EntryPoint = "device_initialize")]
        public static bool InitializeDeviceNative()
        {
            return InitializeDevice(true, false, SystemLanguage.AmericanEnglish, RegionCode.USA, true, true, true, false, "UTC", false);
        }

        [UnmanagedCallersOnly(EntryPoint = "device_reloadFilesystem")]
        public static void ReloadFileSystem()
        {
            SwitchDevice?.ReloadFileSystem();
        }

        public static bool InitializeDevice(bool isHostMapped,
                                            bool useNce,
                                            SystemLanguage systemLanguage,
                                            RegionCode regionCode,
                                            bool enableVsync,
                                            bool enableDockedMode,
                                            bool enablePtc,
                                            bool enableInternetAccess,
                                            string? timeZone,
                                            bool ignoreMissingServices)
        {
            if (SwitchDevice == null)
            {
                return false;
            }

            return SwitchDevice.InitializeContext(isHostMapped,
                                                  useNce,
                                                  systemLanguage,
                                                  regionCode,
                                                  enableVsync,
                                                  enableDockedMode,
                                                  enablePtc,
                                                  enableInternetAccess,
                                                  timeZone,
                                                  ignoreMissingServices);
        }

        [UnmanagedCallersOnly(EntryPoint = "device_load")]
        public static bool LoadApplicationNative(IntPtr pathPtr)
        {
            if(SwitchDevice?.EmulationContext == null)
            {
                return false;
            }

            var path = Marshal.PtrToStringAnsi(pathPtr);

            return LoadApplication(path);
        }

        [UnmanagedCallersOnly(EntryPoint = "device_get_installed_firmware_version")]
        public static IntPtr GetInstalledFirmwareVersionNative()
        {
            var result = GetInstalledFirmwareVersion();
            return Marshal.StringToHGlobalAnsi(result);
        }

        public static void InstallFirmware(Stream stream, bool isXci)
        {
            SwitchDevice?.ContentManager.InstallFirmware(stream, isXci);
        }

        public static string GetInstalledFirmwareVersion()
        {
            var version = SwitchDevice?.ContentManager.GetCurrentFirmwareVersion();

            if (version != null)
            {
                return version.VersionString;
            }

            return String.Empty;
        }

        public static SystemVersion? VerifyFirmware(Stream stream, bool isXci)
        {
            return SwitchDevice?.ContentManager?.VerifyFirmwarePackage(stream, isXci) ?? null;
        }

        public static bool LoadApplication(Stream stream, FileType type, Stream? updateStream = null)
        {
            var emulationContext = SwitchDevice.EmulationContext;
            return type switch
            {
                FileType.None => false,
                FileType.Nsp => emulationContext?.LoadNsp(stream, updateStream) ?? false,
                FileType.Xci => emulationContext?.LoadXci(stream, updateStream) ?? false,
                FileType.Nro => emulationContext?.LoadProgram(stream, true, "") ?? false,
            };
        }

        public static bool LaunchMiiEditApplet()
        {
            string contentPath = SwitchDevice.ContentManager.GetInstalledContentPath(0x0100000000001009, StorageId.BuiltInSystem, NcaContentType.Program);

            return LoadApplication(contentPath);
        }

        public static bool LoadApplication(string? path)
        {
            var emulationContext = SwitchDevice.EmulationContext;

            if (Directory.Exists(path))
            {
                string[] romFsFiles = Directory.GetFiles(path, "*.istorage");

                if (romFsFiles.Length == 0)
                {
                    romFsFiles = Directory.GetFiles(path, "*.romfs");
                }

                if (romFsFiles.Length > 0)
                {
                    Logger.Info?.Print(LogClass.Application, "Loading as cart with RomFS.");

                    if (!emulationContext.LoadCart(path, romFsFiles[0]))
                    {
                        SwitchDevice.DisposeContext();

                        return false;
                    }
                }
                else
                {
                    Logger.Info?.Print(LogClass.Application, "Loading as cart WITHOUT RomFS.");

                    if (!emulationContext.LoadCart(path))
                    {
                        SwitchDevice.DisposeContext();

                        return false;
                    }
                }
            }
            else if (File.Exists(path))
            {
                switch (Path.GetExtension(path).ToLowerInvariant())
                {
                    case ".xci":
                        Logger.Info?.Print(LogClass.Application, "Loading as XCI.");

                        if (!emulationContext.LoadXci(path))
                        {
                            SwitchDevice.DisposeContext();

                            return false;
                        }
                        break;
                    case ".nca":
                        Logger.Info?.Print(LogClass.Application, "Loading as NCA.");

                        if (!emulationContext.LoadNca(path))
                        {
                            SwitchDevice.DisposeContext();

                            return false;
                        }
                        break;
                    case ".nsp":
                    case ".pfs0":
                        Logger.Info?.Print(LogClass.Application, "Loading as NSP.");

                        if (!emulationContext.LoadNsp(path))
                        {
                            SwitchDevice.DisposeContext();

                            return false;
                        }
                        break;
                    default:
                        Logger.Info?.Print(LogClass.Application, "Loading as Homebrew.");
                        try
                        {
                            if (!emulationContext.LoadProgram(path))
                            {
                                SwitchDevice.DisposeContext();

                                return false;
                            }
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            Logger.Error?.Print(LogClass.Application, "The specified file is not supported by Ryujinx.");

                            SwitchDevice.DisposeContext();

                            return false;
                        }
                        break;
                }
            }
            else
            {
                Logger.Warning?.Print(LogClass.Application, $"Couldn't load '{path}'. Please specify a valid XCI/NCA/NSP/PFS0/NRO file.");

                SwitchDevice.DisposeContext();

                return false;
            }

            Translator.IsReadyForTranslation.Reset();

            return true;
        }

        public static void SignalEmulationClose()
        {
            _isStopped = true;
            _isActive = false;

            debug_break(2);
        }

        public static void CloseEmulation()
        {
            if (SwitchDevice == null)
                return;

            _npadManager?.Dispose();
            _npadManager = null;

            _touchScreenManager?.Dispose();
            _touchScreenManager = null;

            SwitchDevice?.InputManager?.Dispose();
            SwitchDevice.InputManager = null;
            _inputManager = null;


            _surfaceEvent?.Set();

            if (Renderer != null)
            {
                _gpuDoneEvent.WaitOne();
                _gpuDoneEvent.Dispose();
                _gpuDoneEvent = null;
                SwitchDevice?.DisposeContext();
                Renderer = null;
            }
        }

        public enum FileType
        {
            None,
            Nsp,
            Xci,
            Nro
        }
    }
}
