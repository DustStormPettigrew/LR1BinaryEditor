using LibLR1.IO;
using LibLR1.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LR1BinaryEditor
{
	static partial class Util
	{
		private const string k_castByte      = "(byte)";
		private const string k_castFloat     = "(float)";
		private const string k_castFract8    = "(f8)";
		private const string k_castFract16   = "(f16)";
		private const string k_castInt       = "(int)";
		private const string k_castUshort    = "(ushort)";
		private const string k_prefixKeyword = "k_";

		private static IFormatProvider ms_cultureInfo = new CultureInfo("en-US");

		public static void RecursiveAppend(LRBinaryReader p_br, Token p_token, ref StringBuilder p_buffer, ref int p_indent, ref int p_sqBracketStack, ref int p_sqBracketCount, string p_format)
		{
			switch (p_token)
			{
				case Token.Fract16:
				{
					Open_Print(ref p_buffer, k_castFract16 + SafeFloatToString(p_br.ReadUShort() / 256f), p_indent, p_sqBracketStack, p_sqBracketCount++);
					break;
				}
				case Token.Fract8:
				{
					Open_Print(ref p_buffer, k_castFract8 + SafeFloatToString(p_br.ReadByte() / 16f), p_indent, p_sqBracketStack, p_sqBracketCount++);
					break;
				}
				case Token.Byte:
				{
					Open_Print(ref p_buffer, k_castByte + p_br.ReadByte().ToString(), p_indent, p_sqBracketStack, p_sqBracketCount++);
					break;
				}
				case Token.Float:
				{
					Open_Print(ref p_buffer, k_castFloat + SafeFloatToString(p_br.ReadFloat()), p_indent, p_sqBracketStack, p_sqBracketCount++);
					break;
				}
				case Token.Int32:
				{
					Open_Print(ref p_buffer, p_br.ReadInt().ToString(), p_indent, p_sqBracketStack, p_sqBracketCount++);
					break;
				}
				case Token.LeftBracket:
				{
					p_sqBracketCount = -1;
					Open_Print(ref p_buffer, "[", p_indent, ++p_sqBracketStack, p_sqBracketCount++);
					break;
				}
				case Token.LeftCurly:
				{
					Open_Print(ref p_buffer, "{", p_indent++, p_sqBracketStack, p_sqBracketCount++);
					break;
				}
				case Token.RightBracket:
				{
					p_sqBracketCount = -1;
					Open_Print(ref p_buffer, "]", p_indent, --p_sqBracketStack, p_sqBracketCount++);
					break;
				}
				case Token.RightCurly:
				{
					Open_Print(ref p_buffer, "}", --p_indent, p_sqBracketStack, p_sqBracketCount++);
					break;
				}
				case Token.String:
				{
					Open_Print(ref p_buffer, "\"" + AddSlashes(p_br.ReadString()) + "\"", p_indent, p_sqBracketStack, p_sqBracketCount++);
					break;
				}
				case Token.UShort:
				{
					Open_Print(ref p_buffer, k_castUshort + p_br.ReadUShort().ToString(), p_indent, p_sqBracketStack, p_sqBracketCount++);
					break;
				}
				default:
				{
					string keyword_info = Util.GetKeywordInfo(p_format, p_token, (p_indent == 0));
					StringBuilder sb = new StringBuilder();
					sb.Append(k_prefixKeyword);
					sb.Append(((byte)(p_token)).ToString("X2"));
					sb.Append(keyword_info.Length > 0 ? "    // " + keyword_info : "");
					Open_Print(ref p_buffer, sb.ToString(), p_indent, p_sqBracketStack, p_sqBracketCount++);
					break;
				}
			}
		}

		private static void Open_Print(ref StringBuilder p_buffer, string p_message, int p_indent = 0, int p_sqBracketStack = 0, int p_sqBracketCount = 0)
		{
			int newIndent = p_indent;
			if (p_sqBracketStack > 0 && p_message[0] != '[')
			{
				newIndent = 0;
			}
			if (p_sqBracketStack == 0 && p_message[0] == ']')
			{
				newIndent = 0;
			}
			p_buffer.Append("".PadLeft(newIndent, '\t'));
			p_buffer.Append(p_sqBracketStack > 0 ? (p_sqBracketCount > 0 ? ", " : "") : "");
			p_buffer.Append(p_message);
			p_buffer.Append(p_sqBracketStack > 0 ? "" : "\r\n");
		}

		public static string GetKeywordInfo(string p_format, Token p_token, bool p_isBlock)
		{
			p_format = p_format.ToUpper();
			if (p_isBlock)
			{
				if (ms_keywordInfoBlocks.ContainsKey(p_format) && ms_keywordInfoBlocks[p_format].ContainsKey(p_token))
				{
					return ms_keywordInfoBlocks[p_format][p_token];
				}
			}
			else
			{
				if (ms_keywordInfoProperties.ContainsKey(p_format) && ms_keywordInfoProperties[p_format].ContainsKey(p_token))
				{
					return ms_keywordInfoProperties[p_format][p_token];
				}
			}
			return "";
		}

		private static string SafeFloatToString(float p_float)
		{
			string s = p_float.ToString("0.0000000000", ms_cultureInfo);
			while (s.EndsWith("0"))
			{
				s = s.Substring(0, s.Length - 1);
			}
			if (s.EndsWith("."))
			{
				s = s.Substring(0, s.Length - 1);
			}
			return s;
		}

		public static string GetFileOpenFilter()
		{
			string bufferAll = "";
			string bufferIndividual = "";
			foreach (KeyValuePair<string, string> kvp in k_fileFormats)
			{
				bufferAll += "*." + kvp.Key + ";";
				bufferIndividual += "|" + (kvp.Value != "" ? kvp.Value : "Unknown_" + kvp.Key) + " (*." + kvp.Key + ")|*." + kvp.Key;
			}
			return "Binary Files|" + bufferAll + bufferIndividual;
		}

		public static MemoryStream Compile(string p_fileData)
		{
			MemoryStream buffer = new MemoryStream();
			string[] lines = p_fileData.Split('\n');
			using (LRBinaryWriter writer = new LRBinaryWriter(buffer, false))
			{
				for (int i = 0; i < lines.Length; i++)
				{
					string line = lines[i].Trim();

					while (line.Length > 0)
					{
						ParseToken(ref line, writer);
						line = line.Trim();
					}
				}
			}
			buffer.Position = 0;
			return buffer;
		}

		private static void ParseToken(ref string p_line, LRBinaryWriter p_writer)
		{
			if (p_line.StartsWith("//"))
			{
				p_line = "";
				return;
			}

			if (p_line[0] == ',')
			{
				p_line = p_line.Substring(1);
			}
			else if (p_line[0] == '{')
			{
				p_writer.WriteToken(Token.LeftCurly);
				p_line = p_line.Substring(1);
			}
			else if (p_line[0] == '}')
			{
				p_writer.WriteToken(Token.RightCurly);
				p_line = p_line.Substring(1);
			}
			else if (p_line[0] == '[')
			{
				p_writer.WriteToken(Token.LeftBracket);
				p_line = p_line.Substring(1);
			}
			else if (p_line[0] == ']')
			{
				p_writer.WriteToken(Token.RightBracket);
				p_line = p_line.Substring(1);
			}
			else if (p_line[0] == '"')
			{
				// seriously I have no idea what I was on when I wrote this
				Match match = Regex.Match(p_line, "\"[^\"\\\\\\r\\n]*(?:\\\\.[^\"\\\\\\r\\n]*)*\"");
				p_writer.WriteStringWithHeader(StripSlashes(match.Value.Substring(1, match.Value.Length - 2)));
				p_line = p_line.Substring(match.Value.Length);
			}
			else if (p_line.StartsWith(k_castFract16))
			{
				p_line = p_line.Substring(k_castFract16.Length);
				float value = ParseFloatToken(ref p_line);
				p_writer.WriteFract16BitWithHeader(new Fract16Bit(value));
			}
			else if (p_line.StartsWith(k_castFract8))
			{
				p_line = p_line.Substring(k_castFract8.Length);
				float value = ParseFloatToken(ref p_line);
				p_writer.WriteFract8BitWithHeader(new Fract8Bit(value));
			}
			else if (p_line.StartsWith(k_castFloat))
			{
				p_line = p_line.Substring(k_castFloat.Length);
				float value = ParseFloatToken(ref p_line);
				p_writer.WriteFloatWithHeader(value);
			}
			else if (p_line.StartsWith(k_castUshort))
			{
				p_line = p_line.Substring(k_castUshort.Length);
				ushort value = ParseUshortToken(ref p_line);
				p_writer.WriteUShortWithHeader(value);
			}
			else if (p_line.StartsWith(k_castByte))
			{
				p_line = p_line.Substring(k_castByte.Length);
				byte value = ParseByteToken(ref p_line);
				p_writer.WriteByteWithHeader(value);
			}
			else if (p_line.StartsWith(k_castInt))
			{
				p_line = p_line.Substring(k_castInt.Length);
				int value = ParseIntToken(ref p_line);
				p_writer.WriteIntWithHeader(value);
			}
			else if (p_line.StartsWith(k_prefixKeyword))
			{
				p_line = p_line.Substring(k_prefixKeyword.Length);
				byte value = Convert.ToByte(p_line.Substring(0, 2), 16);
				p_line = p_line.Substring(2);
				p_writer.WriteByte(value);  // WITHOUT HEADER, YOU RETARD.
			}
			else
			{
				string numeric = ReadNumeric(p_line);
				if (numeric.Length > 0)
				{
					p_line = p_line.Substring(numeric.Length);
					p_writer.WriteIntWithHeader(int.Parse(numeric));
				}
				else
				{
					throw new Exception("Unexpected input: `" + p_line + "`");
				}
			}
		}

		private static float ParseFloatToken(ref string p_line)
		{
			string numeric = ReadNumeric(p_line);
			p_line = p_line.Substring(numeric.Length);
			return float.Parse(numeric, ms_cultureInfo);
		}

		private static ushort ParseUshortToken(ref string p_line)
		{
			string numeric = ReadNumeric(p_line, true);
			p_line = p_line.Substring(numeric.Length);
			return ushort.Parse(numeric);
		}

		private static int ParseIntToken(ref string p_line)
		{
			string numeric = ReadNumeric(p_line, true);
			p_line = p_line.Substring(numeric.Length);
			return int.Parse(numeric);
		}

		private static byte ParseByteToken(ref string p_line)
		{
			string numeric = ReadNumeric(p_line, true);
			p_line = p_line.Substring(numeric.Length);
			return byte.Parse(numeric);
		}

		private static string ReadNumeric(string src, bool p_forceInt = false)
		{
			if (p_forceInt)
			{
				return Regex.Match(src, "-?\\d+").Value;
			}
			return Regex.Match(src, "-?\\d+(\\.\\d+)?").Value;
		}

		// http://www.digitalcoding.com/Code-Snippets/C-Sharp/C-Code-Snippet-AddSlashes-StripSlashes-Escape-String.html

		private static string AddSlashes(string p_input)
		{
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

			string result = p_input;

			try
			{
				result = System.Text.RegularExpressions.Regex.Replace(p_input, @"[\000\010\011\012\015\032\042\047\134\140]", "\\$0");
			}
			catch (Exception Ex)
			{
				// handle any exception here
				Console.WriteLine(Ex.Message);
			}

			return result;
		}

		private static string StripSlashes(string p_input)
		{
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

			string result = p_input;

			try
			{
				result = System.Text.RegularExpressions.Regex.Replace(p_input, @"(\\)([\000\010\011\012\015\032\042\047\134\140])", "$2");
			}
			catch (Exception Ex)
			{
				// handle any exception here
				Console.WriteLine(Ex.Message);
			}

			return result;
		}
	}
}