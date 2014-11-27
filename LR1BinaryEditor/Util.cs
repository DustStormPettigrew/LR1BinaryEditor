using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LibLR1.Utils;

namespace LR1BinaryEditor {
    static partial class Util {
        private const string CAST_BYTE = "(byte)";
        private const string CAST_FLOAT = "(float)";
        private const string CAST_FRACT_8 = "(f8)";
        private const string CAST_FRACT_16 = "(f16)";
        private const string CAST_INT = "(int)";
        private const string CAST_USHORT = "(ushort)";
        private const string PREFIX_KEYWORD = "k_";

        private static IFormatProvider s_CultureInfo = new CultureInfo("en-US");

        public static void RecursiveAppend(BinaryReader br, byte token, ref string buffer, ref int indent, ref int sq_bracket_stack, ref int sq_bracket_count, string format) {
            switch (token) {
                case BinaryFileHelper.TYPE_16BIT_FRACT:
                    Open_Print(ref buffer, CAST_FRACT_16 + SafeFloatToString(br.ReadInt16() / 256f), indent, sq_bracket_stack, sq_bracket_count++);
                    break;
                case BinaryFileHelper.TYPE_8BIT_FRACT:
                    Open_Print(ref buffer, CAST_FRACT_8 + SafeFloatToString(br.ReadByte() / 16f), indent, sq_bracket_stack, sq_bracket_count++);
                    break;
                case BinaryFileHelper.TYPE_BYTE:
                    Open_Print(ref buffer, CAST_BYTE + br.ReadByte().ToString(), indent, sq_bracket_stack, sq_bracket_count++);
                    break;
                case BinaryFileHelper.TYPE_FLOAT:
                    Open_Print(ref buffer, CAST_FLOAT + SafeFloatToString(br.ReadSingle()), indent, sq_bracket_stack, sq_bracket_count++);
                    break;
                case BinaryFileHelper.TYPE_INT32:
                    Open_Print(ref buffer, br.ReadInt32().ToString(), indent, sq_bracket_stack, sq_bracket_count++);
                    break;
                case BinaryFileHelper.TYPE_LEFT_BRACKET:
                    sq_bracket_count = -1;
                    Open_Print(ref buffer, "[", indent, ++sq_bracket_stack, sq_bracket_count++);
                    break;
                case BinaryFileHelper.TYPE_LEFT_CURLY:
                    Open_Print(ref buffer, "{", indent++, sq_bracket_stack, sq_bracket_count++);
                    break;
                case BinaryFileHelper.TYPE_RIGHT_BRACKET:
                    sq_bracket_count = -1;
                    Open_Print(ref buffer, "]", indent, --sq_bracket_stack, sq_bracket_count++);
                    break;
                case BinaryFileHelper.TYPE_RIGHT_CURLY:
                    Open_Print(ref buffer, "}", --indent, sq_bracket_stack, sq_bracket_count++);
                    break;
                case BinaryFileHelper.TYPE_STRING:
                    Open_Print(ref buffer, "\"" + AddSlashes(BinaryFileHelper.ReadString(br.BaseStream)) + "\"", indent, sq_bracket_stack, sq_bracket_count++);
                    break;
                case BinaryFileHelper.TYPE_USHORT:
                    Open_Print(ref buffer, CAST_USHORT + br.ReadUInt16().ToString(), indent, sq_bracket_stack, sq_bracket_count++);
                    break;
                default:
                    string keyword_info = Util.GetKeywordInfo(format, token, (indent == 0));
                    Open_Print(ref buffer, PREFIX_KEYWORD + token.ToString("X2") + (keyword_info.Length > 0 ? "    // " + keyword_info : ""), indent, sq_bracket_stack, sq_bracket_count++);
                    break;
            }
        }

        private static void Open_Print(ref string buffer, string message, int indent = 0, int sq_bracket_stack = 0, int sq_bracket_count = 0) {
            int new_indent = indent;
            if (sq_bracket_stack > 0 && message[0] != '[')
                new_indent = 0;
            if (sq_bracket_stack == 0 && message[0] == ']')
                new_indent = 0;
            buffer += "".PadLeft(new_indent, '\t') + (sq_bracket_stack > 0 ? (sq_bracket_count > 0 ? ", " : "") : "") + message + (sq_bracket_stack > 0 ? "" : "\r\n");
        }

        public static string GetKeywordInfo(string format, byte token, bool isBlock) {
            format = format.ToUpper();
            if (isBlock) {
                if (KeywordInfoBlocks.ContainsKey(format))
                    if (KeywordInfoBlocks[format].ContainsKey(token))
                        return KeywordInfoBlocks[format][token];
            }
            else {
                if (KeywordInfoProperties.ContainsKey(format))
                    if (KeywordInfoProperties[format].ContainsKey(token))
                        return KeywordInfoProperties[format][token];
            }
            return "";
        }

        private static string SafeFloatToString(float f) {
            string s = f.ToString("0.0000000000", s_CultureInfo);
            while (s.EndsWith("0"))
                s = s.Substring(0, s.Length - 1);
            if (s.EndsWith("."))
                s = s.Substring(0, s.Length - 1);
            return s;
        }

        public static string Join(string[] items, string delimiter) {
            string buffer = "";
            for (int i = 0; i < items.Length; i++)
                buffer += (i == 0 ? "" : delimiter) + items[i];
            return buffer;
        }

        public static string GetFileOpenFilter() {
            string buffer_all = "";
            string buffer_individual = "";
            foreach (KeyValuePair<string,string> kvp in FILE_FORMATS) {
                buffer_all += "*." + kvp.Key + ";";
                buffer_individual += "|" + (kvp.Value != "" ? kvp.Value : "Unknown_" + kvp.Key) + " (*." + kvp.Key + ")|*." + kvp.Key;
            }
            return "Binary Files|" + buffer_all + buffer_individual;
        }

        public static MemoryStream Compile(string file_data) {
            MemoryStream buffer = new MemoryStream();
            string[] lines = file_data.Split('\n');
            for (int i = 0; i < lines.Length; i++) {
                string line = lines[i].Trim();

                while (line.Length > 0) {
                    ParseToken(ref line, ref buffer);
                    line = line.Trim();
                }
            }
            buffer.Position = 0;
            return buffer;
        }

        private static void ParseToken(ref string line, ref MemoryStream buffer) {

            /*
             * -- TYPE_16BIT_FRACT
             * -- TYPE_8BIT_FRACT
             * -- TYPE_BYTE
             * -- TYPE_FLOAT
             * TYPE_INT32
             * -- TYPE_LEFT_BRACKET
             * -- TYPE_LEFT_CURLY
             * -- TYPE_RIGHT_BRACKET
             * -- TYPE_RIGHT_CURLY
             * -- TYPE_STRING
             * -- TYPE_USHORT
             * -- KEYWORD
             */

            // die on comments
            if (line.StartsWith("//")) {
                line = "";
                return;
            }

            if (line[0] == ',') {
                line = line.Substring(1);
            }
            else if (line[0] == '{') {
                BinaryFileHelper.WriteByte(buffer, BinaryFileHelper.TYPE_LEFT_CURLY);
                line = line.Substring(1);
            }
            else if (line[0] == '}') {
                BinaryFileHelper.WriteByte(buffer, BinaryFileHelper.TYPE_RIGHT_CURLY);
                line = line.Substring(1);
            }
            else if (line[0] == '[') {
                BinaryFileHelper.WriteByte(buffer, BinaryFileHelper.TYPE_LEFT_BRACKET);
                line = line.Substring(1);
            }
            else if (line[0] == ']') {
                BinaryFileHelper.WriteByte(buffer, BinaryFileHelper.TYPE_RIGHT_BRACKET);
                line = line.Substring(1);
            }
            else if (line[0] == '"') {
                Match match = Regex.Match(line, "\"[^\"\\\\\\r\\n]*(?:\\\\.[^\"\\\\\\r\\n]*)*\"");
                BinaryFileHelper.WriteStringWithHeader(buffer, StripSlashes(match.Value.Substring(1, match.Value.Length - 2)));   // might be iffy, check this before you wreck this.
                line = line.Substring(match.Value.Length);
            }
            else if (line.StartsWith(CAST_FRACT_16)) {
                line = line.Substring(CAST_FRACT_16.Length);
                float value = ParseFloatToken(ref line);
                BinaryFileHelper.WriteFract16BitWithHeader(buffer, new Fract16Bit(value));
            }
            else if (line.StartsWith(CAST_FRACT_8)) {
                line = line.Substring(CAST_FRACT_8.Length);
                float value = ParseFloatToken(ref line);
                BinaryFileHelper.WriteFract8BitWithHeader(buffer, new Fract8Bit(value));
            }
            else if (line.StartsWith(CAST_FLOAT)) {
                line = line.Substring(CAST_FLOAT.Length);
                float value = ParseFloatToken(ref line);
                BinaryFileHelper.WriteFloatWithHeader(buffer, value);
            }
            else if (line.StartsWith(CAST_USHORT)) {
                line = line.Substring(CAST_USHORT.Length);
                ushort value = ParseUshortToken(ref line);
                BinaryFileHelper.WriteUShortWithHeader(buffer, value);
            }
            else if (line.StartsWith(CAST_BYTE)) {
                line = line.Substring(CAST_BYTE.Length);
                byte value = ParseByteToken(ref line);
                BinaryFileHelper.WriteByteWithHeader(buffer, value);
            }
            else if (line.StartsWith(CAST_INT)) {
                line = line.Substring(CAST_INT.Length);
                int value = ParseIntToken(ref line);
                BinaryFileHelper.WriteIntWithHeader(buffer, value);
            }
            else if (line.StartsWith(PREFIX_KEYWORD)) {
                line = line.Substring(PREFIX_KEYWORD.Length);
                byte value = Convert.ToByte(line.Substring(0, 2), 16);
                line = line.Substring(2);
                BinaryFileHelper.WriteByte(buffer, value);  // WITHOUT HEADER, YOU RETARD.
            }
            else {
                string numeric = ReadNumeric(line);
                if (numeric.Length > 0) {
                    line = line.Substring(numeric.Length);
                    BinaryFileHelper.WriteIntWithHeader(buffer, int.Parse(numeric));
                }
                else {
                    throw new Exception("Unexpected input: `" + line + "`");
                }
            }
        }

        private static float ParseFloatToken(ref string line) {
            string numeric = ReadNumeric(line);
            line = line.Substring(numeric.Length);
            return float.Parse(numeric, s_CultureInfo);
        }

        private static ushort ParseUshortToken(ref string line) {
            string numeric = ReadNumeric(line, true);
            line = line.Substring(numeric.Length);
            return ushort.Parse(numeric);
        }

        private static int ParseIntToken(ref string line) {
            string numeric = ReadNumeric(line, true);
            line = line.Substring(numeric.Length);
            return int.Parse(numeric);
        }

        private static byte ParseByteToken(ref string line) {
            string numeric = ReadNumeric(line, true);
            line = line.Substring(numeric.Length);
            return byte.Parse(numeric);
        }

        private static string ReadNumeric(string src, bool forceInt = false) {
            if (forceInt)
                return Regex.Match(src, "-?\\d+").Value;
            return Regex.Match(src, "-?\\d+(\\.\\d+)?").Value;
        }

        // http://www.digitalcoding.com/Code-Snippets/C-Sharp/C-Code-Snippet-AddSlashes-StripSlashes-Escape-String.html

        private static string AddSlashes(string InputTxt) {
            // List of characters handled:
            // \000 null
            // \010 backspace
            // \011 horizontal tab
            // \012 new line
            // \015 carriage return
            // \032 substitute
            // \042 double quote
            // \047 single quote
            // \134 backslash
            // \140 grave accent

            string Result = InputTxt;

            try {
                Result = System.Text.RegularExpressions.Regex.Replace(InputTxt, @"[\000\010\011\012\015\032\042\047\134\140]", "\\$0");
            }
            catch (Exception Ex) {
                // handle any exception here
                Console.WriteLine(Ex.Message);
            }

            return Result;
        }

        private static string StripSlashes(string InputTxt) {
            // List of characters handled:
            // \000 null
            // \010 backspace
            // \011 horizontal tab
            // \012 new line
            // \015 carriage return
            // \032 substitute
            // \042 double quote
            // \047 single quote
            // \134 backslash
            // \140 grave accent

            string Result = InputTxt;

            try {
                Result = System.Text.RegularExpressions.Regex.Replace(InputTxt, @"(\\)([\000\010\011\012\015\032\042\047\134\140])", "$2");
            }
            catch (Exception Ex) {
                // handle any exception here
                Console.WriteLine(Ex.Message);
            }

            return Result;
        }
    }
}
