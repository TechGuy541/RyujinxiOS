using Ryujinx.Common;
using Ryujinx.Common.Memory;
using Ryujinx.Graphics.Texture.Encoders;
using System;
using System.Buffers;

namespace Ryujinx.Graphics.Texture
{
    public static class BCnEncoder
    {
        private const int BlockWidth = 4;
        private const int BlockHeight = 4;

        public static IMemoryOwner<byte> EncodeBC7(Memory<byte> data, int width, int height, int depth, int levels, int layers)
        {
            int size = 0;

            for (int l = 0; l < levels; l++)
            {
                int w = BitUtils.DivRoundUp(Math.Max(1, width >> l), BlockWidth);
                int h = BitUtils.DivRoundUp(Math.Max(1, height >> l), BlockHeight);

                size += w * h * 16 * Math.Max(1, depth >> l) * layers;
            }

            IMemoryOwner<byte> output = ByteMemoryPool.Rent(size);

            int imageBaseIOffs = 0;
            int imageBaseOOffs = 0;

            for (int l = 0; l < levels; l++)
            {
                int w = BitUtils.DivRoundUp(width, BlockWidth);
                int h = BitUtils.DivRoundUp(height, BlockHeight);

                for (int l2 = 0; l2 < layers; l2++)
                {
                    for (int z = 0; z < depth; z++)
                    {
                        BC7Encoder.Encode(
                            output.Memory[imageBaseOOffs..],
                            data[imageBaseIOffs..],
                            width,
                            height,
                            EncodeMode.Fast | EncodeMode.Multithreaded);

                        imageBaseIOffs += width * height * 4;
                        imageBaseOOffs += w * h * 16;
                    }
                }

                width = Math.Max(1, width >> 1);
                height = Math.Max(1, height >> 1);
                depth = Math.Max(1, depth >> 1);
            }

            return output;
        }
    }
}
