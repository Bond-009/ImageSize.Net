using System;
using System.IO;
using System.Linq;

namespace ImageSize
{
    public static class Image
    {
        // All magic byte arrays are missing their first byte
        private static readonly byte[] GIF_MAGIC_START = new byte[] { 0x49, 0x46, 0x38 };
        private static readonly byte[] PNG_MAGIC = new byte[] { 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
        private static readonly byte[] MNG_MAGIC = new byte[] { 0x4d, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };

        /// <summary>
        /// Retrieves the size of the image without reading the entire file.
        /// </summary>
        /// <param name="path">path to the image</param>
        /// <returns>The width and height of the image</returns>
        public static (int width, int height) GetSize(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            using (Stream str = File.OpenRead(path))
            {
                return GetSize(str);
            }
        }

        /// <summary>
        /// Retrieves the size of the image without reading the entire file.
        /// </summary>
        /// <param name="stream">IO stream of the image</param>
        /// <returns>The width and height of the image</returns>
        public static (int width, int height) GetSize(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using (BinaryReader reader = new BinaryReader(stream))
            {
                return GetSize(reader);
            }
        }

        /// <summary>
        /// Retrieves the size of the image without reading the entire file.
        /// </summary>
        /// <param name="reader">BinaryReader for the IO stream of the image</param>
        /// <returns>The width and height of the image</returns>
        public static (int width, int height) GetSize(BinaryReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            switch(reader.ReadByte())
            {
                // Magic BMP bytes:
                // Hex   | String | Note
                // ------|--------|--------------
                // 42 4d | BM     |
                // 42 41 | BA     | Not Supported
                case 0x42:
                {
                    byte letter2 = reader.ReadByte();
                    if (letter2 == 0x4d)
                    {
                        return GetBmpSize(reader);
                    }
                    else if (letter2 == 0x41)
                    {
                        throw new NotSupportedException("OS/2 struct bitmap array are not supported");
                    }
                    break;
                }
                // Magic GIF bytes:
                // 47 49 46 38 37 61
                // 47 49 46 38 39 61
                case (0x47):
                {
                    byte[] bytes = reader.ReadBytes(3);
                    if (bytes.SequenceEqual(GIF_MAGIC_START))
                    {
                        byte year = reader.ReadByte();
                        if ((year == 0x39 || year == 0x37) // 9 or 7
                            && reader.ReadByte() == 0x61) // a
                        {
                            return GetGifSize(reader);
                        }
                    }
                    break;
                }
                // Magic PNG bytes: 89 50 4e 47 0d 0a 1a 0a
                case 0x89:
                {
                    byte[] bytes = reader.ReadBytes(7);
                    if (bytes.SequenceEqual(PNG_MAGIC))
                    {
                        return GetPngSize(reader);
                    }
                    break;
                }
                // Magic MNG bytes: 8a 4d 4e 47 0d 0a 1a 0a
                case 0x8a:
                {
                    byte[] bytes = reader.ReadBytes(7);
                    if (bytes.SequenceEqual(MNG_MAGIC))
                    {
                        return GetPngSize(reader);
                    }
                    break;
                }
                // Magic JPEG bytes: ff d8 ff
                case 0xff:
                {
                    if (reader.ReadByte() == 0xd8
                        && reader.ReadByte() == 0xff)
                    {
                        return GetJpegSize(reader);
                    }
                    break;
                }
                default: break;
            }
            throw new NotSupportedException();
        }

        internal static (ushort width, ushort height) GetGifSize(BinaryReader reader)
        {
            ushort width = reader.ReadUInt16();
            ushort height = reader.ReadUInt16();
            return (width, height);
        }

        internal static (int width, int height) GetPngSize(BinaryReader reader)
        {
            // Read length and Chunk Type
            // We don't care about it, since the first one is always MHDR (PNG) or MHDR (MNG)
            // Which contains it contains the image's width (4 bytes), height (4 bytes),
            // bit depth (1 byte), color type (1 byte), compression method (1 byte),
            // filter method (1 byte), and interlace method (1 byte)
            reader.ReadBytes(8);
            int width = reader.ReadBigEndianInt32();
            int height = reader.ReadBigEndianInt32();
            return (width, height);
        }

        internal static (int width, int height) GetBmpSize(BinaryReader reader)
        {
            // Read remaining file header bytes (12)
            reader.ReadBytes(12);
            // Read information header length
            // 12 -> BITMAPCOREHEADER
            // 40 -> BITMAPINFOHEADER
            // 64 -> OS22XBITMAPHEADER
            int infoLen = reader.ReadInt32();
            if (infoLen == 12 || infoLen == 64)
            {
                ushort width = reader.ReadUInt16();
                ushort height = reader.ReadUInt16();
                return (width, height);
            }
            else if (infoLen == 40)
            {
                int width = reader.ReadInt32();
                int height = reader.ReadInt32();
                return (width, height);
            }
            throw new Exception($"Unsupported info header size ({infoLen})");
        }

        internal static (ushort width, ushort height) GetJpegSize(BinaryReader reader)
        {
            do
            {
                byte marker = reader.ReadByte();
                if (marker == 0x00)
                {
                    // Within the entropy-coded data, after any 0xFF byte,
                    // a 0x00 byte is inserted by the encoder before the next byte,
                    // so that there does not appear to be a marker where none is intended,
                    // preventing framing errors.
                    continue;
                }
                else if (marker == 0xd9)
                {
                    // End Of Image
                    throw new Exception("No size markers found before EOF in the JPG");
                }

                // The length includes the two bytes for the length, but not the two bytes for the marker
                ushort frameLength = reader.ReadBigEndianUInt16();
                // SOF0, SOF1, SOF2, SOF3 markers specify width and height
                if (marker == 0xc0 || marker == 0xc1 || marker == 0xc2 || marker == 0xc3)
                {
                    reader.ReadByte(); // We don't care about the first byte
                    ushort height = reader.ReadBigEndianUInt16();
                    ushort width = reader.ReadBigEndianUInt16();
                    return (width, height);
                }

                // Read everything until the start of the next marker
                reader.ReadBytes(frameLength - 2);
            } while (reader.ReadByte() == 0xff);

            throw new Exception("No size markers found in the JPG");
        }

        internal static ushort ReadBigEndianUInt16(this BinaryReader reader)
        {
            byte[] buf = reader.ReadBytes(2);
            return (ushort)((buf[0]<<8) | buf[1]);
        }

        internal static int ReadBigEndianInt32(this BinaryReader reader)
        {
            byte[] buf = reader.ReadBytes(4);
            return ((buf[0]<<24) | (buf[1]<<16) | (buf[2]<<8) | buf[3]);
        }
    }
}
