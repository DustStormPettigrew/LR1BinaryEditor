using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LibLR1.Utils {
    public static class BinaryFileHelper {
        public const byte
            TYPE_STRING = 0x02,
            TYPE_FLOAT = 0x03,
            TYPE_INT32 = 0x04,
            TYPE_LEFT_CURLY = 0x05,
            TYPE_RIGHT_CURLY = 0x06,
            TYPE_LEFT_BRACKET = 0x07,
            TYPE_RIGHT_BRACKET = 0x08,
            TYPE_8BIT_FRACT = 0x0B,
            TYPE_BYTE = 0x0C,
            TYPE_16BIT_FRACT = 0x0D,
            TYPE_USHORT = 0x0E,
            TYPE_ARRAY = 0x14,      // compression pass only
            TYPE_STRUCT = 0x16;     // compression pass only

        private const int
            SIZE_32 = 4,
            SIZE_16 = 2;

        #region Compression

        public static MemoryStream Decompress(string path) {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                return Decompress(fs);
        }

        public static MemoryStream Decompress(Stream streamIn) {
            Dictionary<byte, byte[]> structs = new Dictionary<byte, byte[]>();
            MemoryStream msOut = new MemoryStream();
            while (streamIn.Position < streamIn.Length) {
                byte block_id = ReadByte(streamIn);
                RecursiveDecompress(block_id, streamIn, msOut, structs);
            }
            msOut.Position = 0;
            return msOut;
        }

        private static void RecursiveDecompress(byte block_id, Stream streamIn, Stream streamOut, Dictionary<byte, byte[]> structs) {
            switch (block_id) {
                case TYPE_STRING: { // block_id followed by a null-terminated ascii string
                        streamOut.WriteByte(block_id);
                        while (true) {
                            byte buffer = ReadByte(streamIn);
                            streamOut.WriteByte(buffer);
                            if (buffer == 0)
                                break;
                        }
                    } break;
                case TYPE_FLOAT:
                case TYPE_INT32: {  // 32-bit little-endian IEEE float or int32
                        streamOut.WriteByte(block_id);
                        byte[] buffer = new byte[SIZE_32];
                        streamIn.Read(buffer, 0, buffer.Length);
                        streamOut.Write(buffer, 0, buffer.Length);
                    } break;
                case TYPE_LEFT_CURLY:
                case TYPE_RIGHT_CURLY:
                case TYPE_LEFT_BRACKET:
                case TYPE_RIGHT_BRACKET: {  // just copy the token, nothing else
                        streamOut.WriteByte(block_id);
                    } break;
                case TYPE_8BIT_FRACT:
                case TYPE_BYTE: {           // copy a single byte from the stream
                        streamOut.WriteByte(block_id);
                        byte buffer = ReadByte(streamIn);
                        streamOut.WriteByte(buffer);
                    } break;
                case TYPE_16BIT_FRACT:
                case TYPE_USHORT: {         // copy 16 bits (2 bytes) from the stream
                        streamOut.WriteByte(block_id);
                        byte[] buffer = new byte[SIZE_16];
                        streamIn.Read(buffer, 0, buffer.Length);
                        streamOut.Write(buffer, 0, buffer.Length);
                    } break;
                case TYPE_ARRAY: {          // decompression pass.
                        short arraylen = ReadShort(streamIn);
                        byte arraytype = (byte)streamIn.ReadByte();
                        for (int i = 0; i < arraylen; i++)
                            RecursiveDecompress(arraytype, streamIn, streamOut, structs);
                    } break;
                case TYPE_STRUCT: {         // decompression pass
                        byte structid = ReadByte(streamIn);
                        byte structlen = ReadByte(streamIn);
                        byte[] structdef = new byte[structlen];
                        streamIn.Read(structdef, 0, structlen);
                        structs.Add(structid, structdef);
                    } break;
                default: {
                        if (structs.ContainsKey(block_id))  // it's a struct
                            for (int i = 0; i < structs[block_id].Length; i++)
                                RecursiveDecompress(structs[block_id][i], streamIn, streamOut, structs);
                        else
                            streamOut.WriteByte(block_id);  // it's a file-specific block token
                    } break;
            }
        }

        #endregion

        #region Basic Read

        public static byte ReadByte(Stream stream) {
            return (byte)stream.ReadByte();
        }

        public static sbyte ReadSByte(Stream stream) {
            return (sbyte)stream.ReadByte();
        }

        public static byte ReadByteWithHeader(Stream stream) {
            Expect(stream, TYPE_BYTE);
            return ReadByte(stream);
        }

        public static Fract8Bit ReadFract8BitWithHeader(Stream stream) {
            Expect(stream, TYPE_8BIT_FRACT);
            return Fract8Bit.FromStream(stream);
        }

        public static ushort ReadUShort(Stream stream) {
            byte[] b_val = new byte[sizeof(ushort)];
            stream.Read(b_val, 0, b_val.Length);
            return BitConverter.ToUInt16(b_val, 0);
        }

        public static short ReadShort(Stream stream) {
            byte[] b_val = new byte[sizeof(short)];
            stream.Read(b_val, 0, b_val.Length);
            return BitConverter.ToInt16(b_val, 0);
        }

        public static Fract16Bit ReadFract16BitWithHeader(Stream stream) {
            Expect(stream, TYPE_16BIT_FRACT);
            return Fract16Bit.FromStream(stream);
        }

        public static ushort ReadUShortWithHeader(Stream stream) {
            Expect(stream, TYPE_USHORT);
            return ReadUShort(stream);
        }

        public static uint ReadUInt(Stream stream) {
            byte[] b_val = new byte[sizeof(uint)];
            stream.Read(b_val, 0, b_val.Length);
            return BitConverter.ToUInt32(b_val, 0);
        }

        public static int ReadInt(Stream stream) {
            byte[] b_val = new byte[sizeof(int)];
            stream.Read(b_val, 0, b_val.Length);
            return BitConverter.ToInt32(b_val, 0);
        }

        public static int ReadIntWithHeader(Stream stream) {
            Expect(stream, TYPE_INT32);
            return ReadInt(stream);
        }

        public static float ReadFloat(Stream stream) {
            byte[] b_val = new byte[sizeof(float)];
            stream.Read(b_val, 0, b_val.Length);
            return BitConverter.ToSingle(b_val, 0);
        }

        public static float ReadFloatWithHeader(Stream stream) {
            Expect(stream, TYPE_FLOAT);
            return ReadFloat(stream);
        }

        public static string ReadString(Stream stream) {
            string s_val = "";
            byte b;
            while ((b = ReadByte(stream)) != 0x00)
                s_val += (char)b;
            return s_val;
        }

        public static string ReadStringWithHeader(Stream stream) {
            Expect(stream, TYPE_STRING);
            return ReadString(stream);
        }

        #endregion

        #region Complex Read

        public delegate T ReadObject<T>(Stream stream);

        public static T[] ReadArrayBlock<T>(Stream stream, ReadObject<T> readFunc) {
            Expect(stream, TYPE_LEFT_BRACKET);
            int array_len = ReadIntWithHeader(stream);
            T[] output = new T[array_len];
            Expect(stream, TYPE_RIGHT_BRACKET);
            Expect(stream, TYPE_LEFT_CURLY);
            for (int i = 0; i < array_len; i++)
                output[i] = readFunc(stream);
            Expect(stream, TYPE_RIGHT_CURLY);
            return output;
        }

        public static List<T> ReadListBlock<T>(Stream stream, ReadObject<T> readFunc) {
            Expect(stream, TYPE_LEFT_BRACKET);
            int array_len = ReadIntWithHeader(stream);
            List<T> output = new List<T>();
            Expect(stream, TYPE_RIGHT_BRACKET);
            Expect(stream, TYPE_LEFT_CURLY);
            for (int i = 0; i < array_len; i++)
                output.Add(readFunc(stream));
            Expect(stream, TYPE_RIGHT_CURLY);
            return output;
        }

        public static T[] ReadStructArrayBlock<T>(Stream stream, ReadObject<T> readFunc, byte typeByte) {
            Expect(stream, TYPE_LEFT_BRACKET);
            int array_len = ReadIntWithHeader(stream);
            T[] output = new T[array_len];
            Expect(stream, TYPE_RIGHT_BRACKET);
            Expect(stream, TYPE_LEFT_CURLY);
            for (int i = 0; i < array_len; i++) {
                Expect(stream, typeByte);
                output[i] = ReadStruct<T>(stream, readFunc);
            }
            Expect(stream, TYPE_RIGHT_CURLY);
            return output;
        }

        public static List<T> ReadStructListBlock<T>(Stream stream, ReadObject<T> readFunc, byte typeByte) {
            Expect(stream, TYPE_LEFT_BRACKET);
            int array_len = ReadIntWithHeader(stream);
            List<T> output = new List<T>();
            Expect(stream, TYPE_RIGHT_BRACKET);
            Expect(stream, TYPE_LEFT_CURLY);
            for (int i = 0; i < array_len; i++) {
                Expect(stream, typeByte);
                output.Add(ReadStruct<T>(stream, readFunc));
            }
            Expect(stream, TYPE_RIGHT_CURLY);
            return output;
        }

        public static Dictionary<string, T> ReadDictionaryBlock<T>(Stream stream, ReadObject<T> readFunc, byte typeByte) {
            Dictionary<string, T> output = new Dictionary<string, T>();
            Expect(stream, TYPE_LEFT_BRACKET);
            int dict_len = ReadIntWithHeader(stream);
            Expect(stream, TYPE_RIGHT_BRACKET);
            Expect(stream, TYPE_LEFT_CURLY);
            for (int i = 0; i < dict_len; i++) {
                Expect(stream, typeByte);
                string item_key = i.ToString();
                if (Next(stream, TYPE_STRING))
                    item_key = ReadStringWithHeader(stream);
                T item_value = ReadStruct<T>(stream, readFunc);
                output.Add(item_key, item_value);
            }
            Expect(stream, TYPE_RIGHT_CURLY);
            return output;
        }

        public static T ReadStruct<T>(Stream stream, ReadObject<T> readFunc) {
            Expect(stream, TYPE_LEFT_CURLY);
            T output = readFunc(stream);
            Expect(stream, TYPE_RIGHT_CURLY);
            return output;
        }

        public static byte[] ReadByteArrayBlock(Stream stream) {
            return ReadArrayBlock<byte>(stream, new ReadObject<byte>(ReadByteWithHeader));
        }

        public static float[] ReadFloatArrayBlock(Stream stream) {
            return ReadArrayBlock<float>(stream, new ReadObject<float>(ReadFloatWithHeader));
        }

        public static int[] ReadIntArrayBlock(Stream stream) {
            return ReadArrayBlock<int>(stream, new ReadObject<int>(ReadIntWithHeader));
        }

        public static LRQuaternion[] ReadQuaternionArrayBlock(Stream stream) {
            return ReadArrayBlock<LRQuaternion>(stream, new ReadObject<LRQuaternion>(LRQuaternion.FromStream));
        }

        public static string[] ReadStringArrayBlock(Stream stream) {
            return ReadArrayBlock<string>(stream, new ReadObject<string>(ReadStringWithHeader));
        }

        public static LRVector2[] ReadVector2fArrayBlock(Stream stream) {
            return ReadArrayBlock<LRVector2>(stream, new ReadObject<LRVector2>(LRVector2.FromStream));
        }

        public static LRVector3[] ReadVector3fArrayBlock(Stream stream) {
            return ReadArrayBlock<LRVector3>(stream, new ReadObject<LRVector3>(LRVector3.FromStream));
        }

        #endregion

        #region Basic Write

        public static void WriteByte(Stream stream, byte value) {
            stream.WriteByte(value);
        }

        public static void WriteSByte(Stream stream, sbyte value) {
            stream.WriteByte((byte)value);
        }

        public static void WriteByteWithHeader(Stream stream, byte value) {
            stream.WriteByte(TYPE_BYTE);
            WriteByte(stream, value);
        }

        public static void WriteFract8BitWithHeader(Stream stream, Fract8Bit value) {
            stream.WriteByte(TYPE_8BIT_FRACT);
            WriteSByte(stream, value.Value);
        }

        public static void WriteUShort(Stream stream, ushort value) {
            byte[] b_val = BitConverter.GetBytes(value);
            stream.Write(b_val, 0, b_val.Length);
        }

        public static void WriteShort(Stream stream, short value) {
            byte[] b_val = BitConverter.GetBytes(value);
            stream.Write(b_val, 0, b_val.Length);
        }

        public static void WriteFract16BitWithHeader(Stream stream, Fract16Bit value) {
            stream.WriteByte(TYPE_16BIT_FRACT);
            WriteShort(stream, value.Value);
        }

        public static void WriteUShortWithHeader(Stream stream, ushort value) {
            stream.WriteByte(TYPE_USHORT);
            WriteUShort(stream, value);
        }

        public static void WriteUInt(Stream stream, uint value) {
            byte[] b_val = BitConverter.GetBytes(value);
            stream.Write(b_val, 0, b_val.Length);
        }

        public static void WriteInt(Stream stream, int value) {
            byte[] b_val = BitConverter.GetBytes(value);
            stream.Write(b_val, 0, b_val.Length);
        }

        public static void WriteIntWithHeader(Stream stream, int value) {
            stream.WriteByte(TYPE_INT32);
            WriteInt(stream, value);
        }

        public static void WriteFloat(Stream stream, float value) {
            byte[] b_val = BitConverter.GetBytes(value);
            stream.Write(b_val, 0, b_val.Length);
        }

        public static void WriteFloatWithHeader(Stream stream, float value) {
            stream.WriteByte(TYPE_FLOAT);
            WriteFloat(stream, value);
        }

        public static void WriteString(Stream stream, string value) {
            byte[] b_val = Encoding.ASCII.GetBytes(value);
            stream.Write(b_val, 0, b_val.Length);
            stream.WriteByte(0x00);
        }

        public static void WriteStringWithHeader(Stream stream, string value) {
            stream.WriteByte(TYPE_STRING);
            WriteString(stream, value);
        }

        #endregion

        #region Complex Write

        public delegate void WriteObject<T>(Stream stream, T value);

        public static void WriteArrayBlock<T>(Stream stream, WriteObject<T> writeFunc, T[] values) {
            stream.WriteByte(TYPE_LEFT_BRACKET);
            WriteIntWithHeader(stream, values.Length);
            stream.WriteByte(TYPE_RIGHT_BRACKET);
            stream.WriteByte(TYPE_LEFT_CURLY);
            for (int i = 0; i < values.Length; i++)
                writeFunc(stream, values[i]);
            stream.WriteByte(TYPE_RIGHT_CURLY);
        }

        public static void WriteListBlock<T>(Stream stream, WriteObject<T> writeFunc, List<T> values) {
            stream.WriteByte(TYPE_LEFT_BRACKET);
            WriteIntWithHeader(stream, values.Count);
            stream.WriteByte(TYPE_RIGHT_BRACKET);
            stream.WriteByte(TYPE_LEFT_CURLY);
            for (int i = 0; i < values.Count; i++)
                writeFunc(stream, values[i]);
            stream.WriteByte(TYPE_RIGHT_CURLY);
        }

        public static void WriteStructArrayBlock<T>(Stream stream, WriteObject<T> writeFunc, T[] values, byte typeByte) {
            stream.WriteByte(TYPE_LEFT_BRACKET);
            WriteIntWithHeader(stream, values.Length);
            stream.WriteByte(TYPE_RIGHT_BRACKET);
            stream.WriteByte(TYPE_LEFT_CURLY);
            for (int i = 0; i < values.Length; i++) {
                stream.WriteByte(typeByte);
                WriteStruct<T>(stream, writeFunc, values[i]);
            }
            stream.WriteByte(TYPE_RIGHT_CURLY);
        }

        public static void WriteStructListBlock<T>(Stream stream, WriteObject<T> writeFunc, List<T> values, byte typeByte) {
            stream.WriteByte(TYPE_LEFT_BRACKET);
            WriteIntWithHeader(stream, values.Count);
            stream.WriteByte(TYPE_RIGHT_BRACKET);
            stream.WriteByte(TYPE_LEFT_CURLY);
            for (int i = 0; i < values.Count; i++) {
                stream.WriteByte(typeByte);
                WriteStruct<T>(stream, writeFunc, values[i]);
            }
            stream.WriteByte(TYPE_RIGHT_CURLY);
        }

        public static void WriteDictionaryBlock<T>(Stream stream, WriteObject<T> writeFunc, Dictionary<string, T> values, byte typeByte) {
            stream.WriteByte(TYPE_LEFT_BRACKET);
            WriteIntWithHeader(stream, values.Count);
            stream.WriteByte(TYPE_RIGHT_BRACKET);
            stream.WriteByte(TYPE_LEFT_CURLY);
            foreach (KeyValuePair<string, T> kvp in values) {
                stream.WriteByte(typeByte);
                WriteStringWithHeader(stream, kvp.Key);
                WriteStruct<T>(stream, writeFunc, kvp.Value);
            }
            stream.WriteByte(TYPE_RIGHT_CURLY);
        }

        public static void WriteStruct<T>(Stream stream, WriteObject<T> writeFunc, T value) {
            stream.WriteByte(TYPE_LEFT_CURLY);
            writeFunc(stream, value);
            stream.WriteByte(TYPE_RIGHT_CURLY);
        }

        public static void WriteByteArrayBlock(Stream stream, byte[] values) {
            WriteArrayBlock<byte>(stream, new WriteObject<byte>(WriteByteWithHeader), values);
        }

        public static void WriteFloatArrayBlock(Stream stream, float[] values) {
            WriteArrayBlock<float>(stream, new WriteObject<float>(WriteFloatWithHeader), values);
        }

        public static void WriteIntArrayBlock(Stream stream, int[] values) {
            WriteArrayBlock<int>(stream, new WriteObject<int>(WriteIntWithHeader), values);
        }

        public static void WriteQuaternionArrayBlock(Stream stream, LRQuaternion[] values) {
            WriteArrayBlock<LRQuaternion>(stream, new WriteObject<LRQuaternion>(LRQuaternion.ToStream), values);
        }

        public static void WriteStringArrayBlock(Stream stream, string[] values) {
            WriteArrayBlock<string>(stream, new WriteObject<string>(WriteStringWithHeader), values);
        }

        public static void WriteVector2fArrayBlock(Stream stream, LRVector2[] values) {
            WriteArrayBlock<LRVector2>(stream, new WriteObject<LRVector2>(LRVector2.ToStream), values);
        }

        public static void WriteVector3fArrayBlock(Stream stream, LRVector3[] values) {
            WriteArrayBlock<LRVector3>(stream, new WriteObject<LRVector3>(LRVector3.ToStream), values);
        }

        #endregion

        #region Expect/Next

        public static void Expect(Stream stream, byte expected) {
            byte actual = ReadByte(stream);
            if (actual != expected)
                throw new Exception("Invalid data. Expected 0x" + expected.ToString("X2") + ", got 0x" + actual.ToString("X2") + ".");
        }

        public static byte Expect(Stream stream, byte[] expected) {
            byte actual = ReadByte(stream);
            if (!expected.Contains(actual)) {
                string s_expected = "{ ";
                for (int i = 0; i < expected.Length; i++)
                    s_expected += (i == 0 ? "" : ", ") + "0x" + expected[i].ToString("X2");
                s_expected += " }";
                throw new Exception("Invalid data. Expected " + s_expected + ", got 0x" + actual.ToString("X2") + ".");
            }
            return actual;
        }

        public static bool Next(Stream stream, byte expected) {
            byte actual = ReadByte(stream);
            stream.Position--;
            return actual == expected;
        }

        #endregion
    }
}