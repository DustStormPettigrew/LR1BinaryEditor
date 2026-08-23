using LibLR1.Inspection;
using LibLR1.IO;
using LibLR1.Schema;
using LibLR1.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LR1BinaryEditor
{
	internal enum BinaryEditorDocumentState
	{
		ValidSemantic,
		InspectionOnly,
		RawOpaque
	}

	internal enum BinaryEditorEncoding
	{
		Tokenized,
		RawOpaque,
		Unregistered
	}

	internal sealed class BinaryDiffSummary
	{
		public int SourceLength { get; init; }
		public int CandidateLength { get; init; }
		public long? FirstDifferenceOffset { get; init; }
		public bool Identical => FirstDifferenceOffset == null && SourceLength == CandidateLength;

		public override string ToString()
		{
			string offset = Identical ? "none (identical)" : "0x" + FirstDifferenceOffset.GetValueOrDefault().ToString("X");
			return $"Source: {SourceLength:N0} bytes\nCandidate: {CandidateLength:N0} bytes\nFirst difference: {offset}";
		}
	}

	internal sealed class TokenOffsetMap
	{
		private readonly List<(long BinaryOffset, int TextOffset)> m_entries = new List<(long, int)>();

		public void Add(long p_binaryOffset, int p_textOffset) => m_entries.Add((p_binaryOffset, p_textOffset));

		public int FindTextOffset(long p_binaryOffset)
		{
			if (m_entries.Count == 0) return 0;
			(long BinaryOffset, int TextOffset) best = m_entries[0];
			foreach ((long binaryOffset, int textOffset) in m_entries)
			{
				if (binaryOffset > p_binaryOffset) break;
				best = (binaryOffset, textOffset);
			}
			return best.TextOffset;
		}
	}

	internal sealed class BinaryEditorDocumentSession
	{
		public string Format { get; internal set; }
		public string FileName { get; internal set; }
		public BinaryEditorEncoding Encoding { get; internal set; }
		public BinaryEditorDocumentState State { get; internal set; }
		public byte[] SourceData { get; internal set; }
		public byte[] DecompressedData { get; internal set; }
		public byte[] CandidateData { get; internal set; }
		public byte[] CandidateDecompressedData { get; internal set; }
		public string Text { get; internal set; }
		public string EvidenceStatus { get; internal set; }
		public FormatInspectionResult<object> Inspection { get; internal set; }
		public TokenOffsetMap OffsetMap { get; internal set; } = new TokenOffsetMap();
		public BinaryDiffSummary SourceDiff { get; internal set; }
		public BinaryDiffSummary DecompressedDiff { get; internal set; }
		public Exception CompileException { get; internal set; }

		public FormatInspectionIssue Issue => Inspection?.Issue;
		public bool CanWrite => Inspection?.CanWrite == true && (State == BinaryEditorDocumentState.ValidSemantic || State == BinaryEditorDocumentState.RawOpaque);
		public bool CanExportJson => State == BinaryEditorDocumentState.ValidSemantic && Inspection?.CanWrite == true;
		public bool CanEditTokenText => Encoding == BinaryEditorEncoding.Tokenized;
		public bool CanEditRawHex => Encoding == BinaryEditorEncoding.RawOpaque && State == BinaryEditorDocumentState.InspectionOnly;
		public bool CanEditText => CanEditTokenText || CanEditRawHex;

		public string Diagnostic
		{
			get
			{
				if (CompileException != null) return "Compile error: " + CompileException.Message;
				if (Issue == null) return null;
				string token = Issue.TokenId.HasValue ? $" token 0x{Issue.TokenId.Value:X2}" : string.Empty;
				string offset = Issue.DecompressedOffset.HasValue ? $" at decompressed offset 0x{Issue.DecompressedOffset.Value:X}" : string.Empty;
				return $"{Issue.Kind}:{token}{offset}. {Issue.Message}";
			}
		}
	}

	internal sealed class BinaryEditorDocumentService
	{
		public IReadOnlyList<string> RegisteredFormats => SchemaStructureProvider.Formats;

		public BinaryEditorDocumentSession Open(string p_path, string p_fileName = null)
		{
			if (string.IsNullOrWhiteSpace(p_path)) throw new ArgumentException("A file path is required.", nameof(p_path));
			string format = NormalizeFormat(Path.GetExtension(p_path));
			byte[] source = File.ReadAllBytes(p_path);
			if (!SchemaStructureProvider.Formats.Contains(format, StringComparer.OrdinalIgnoreCase))
			{
				return CreateOpaque(format, p_fileName ?? Path.GetFileName(p_path), source, null);
			}

			FormatInspectionResult<object> inspection = FormatInspection.ReadRegistered(format, p_path);
			BinaryEditorEncoding encoding = SchemaStructureProvider.UsesTokenizedEncoding(format)
				? BinaryEditorEncoding.Tokenized
				: BinaryEditorEncoding.RawOpaque;
			return CreateSession(format, p_fileName ?? Path.GetFileName(p_path), encoding, inspection);
		}

		public BinaryEditorDocumentSession Validate(BinaryEditorDocumentSession p_session, string p_text)
		{
			if (p_session == null) throw new ArgumentNullException(nameof(p_session));
			if (p_session.Encoding == BinaryEditorEncoding.Unregistered)
			{
				throw new InvalidOperationException("This exact opaque document is not editable.");
			}

			p_session.Text = p_text ?? string.Empty;
			p_session.CompileException = null;
			p_session.CandidateData = null;
			p_session.CandidateDecompressedData = null;
			string candidatePath = CreateTemporaryPath(p_session.Format);
			string outputPath = CreateTemporaryPath(p_session.Format);
			try
			{
				p_session.CandidateDecompressedData = p_session.Encoding == BinaryEditorEncoding.Tokenized
					? CompileTokenText(p_session.Text)
					: CompileHex(p_session.Text);
				File.WriteAllBytes(candidatePath, p_session.CandidateDecompressedData);
				FormatInspectionResult<object> inspection = FormatInspection.ReadRegistered(p_session.Format, candidatePath);
				p_session.Inspection = inspection;
				if (inspection.CanWrite)
				{
					FormatInspection.WriteRegistered(inspection, outputPath);
					p_session.CandidateData = File.ReadAllBytes(outputPath);
					p_session.State = p_session.Encoding == BinaryEditorEncoding.Tokenized ? BinaryEditorDocumentState.ValidSemantic : BinaryEditorDocumentState.RawOpaque;
				}
				else
				{
					p_session.State = BinaryEditorDocumentState.InspectionOnly;
				}
			}
			catch (Exception exception)
			{
				p_session.CompileException = exception;
				p_session.Inspection = null;
				p_session.State = BinaryEditorDocumentState.InspectionOnly;
			}
			finally
			{
				TryDelete(candidatePath);
				TryDelete(outputPath);
			}

			p_session.SourceDiff = Compare(p_session.SourceData, p_session.CandidateData ?? p_session.CandidateDecompressedData);
			p_session.DecompressedDiff = Compare(p_session.DecompressedData, p_session.CandidateDecompressedData);
			return p_session;
		}

		public BinaryEditorDocumentSession CreateCandidate(string p_format, string p_fileName, string p_text)
		{
			string format = NormalizeFormat(p_format);
			if (!SchemaStructureProvider.Formats.Contains(format, StringComparer.OrdinalIgnoreCase) || !SchemaStructureProvider.UsesTokenizedEncoding(format))
			{
				throw new InvalidOperationException("The selected output is not a registered tokenized LibLR1 format.");
			}
			BinaryEditorDocumentSession session = new BinaryEditorDocumentSession
			{
				Format = format,
				FileName = p_fileName,
				Encoding = BinaryEditorEncoding.Tokenized,
				State = BinaryEditorDocumentState.InspectionOnly,
				SourceData = Array.Empty<byte>(),
				DecompressedData = Array.Empty<byte>(),
				Text = p_text ?? string.Empty,
				EvidenceStatus = BinaryEditorMetadataCatalog.GetEvidenceStatus(format)
			};
			return Validate(session, session.Text);
		}

		public void Write(BinaryEditorDocumentSession p_session, string p_text, string p_path)
		{
			if (p_session == null) throw new ArgumentNullException(nameof(p_session));
			if (p_session.Encoding != BinaryEditorEncoding.Unregistered) Validate(p_session, p_text);
			if (!p_session.CanWrite) throw new InvalidOperationException(p_session.Diagnostic ?? "The document is not validated for writing.");

			string temporaryPath = CreateTemporaryPath(p_session.Format);
			try
			{
				FormatInspection.WriteRegistered(p_session.Inspection, temporaryPath);
				File.Copy(temporaryPath, p_path, true);
			}
			finally { TryDelete(temporaryPath); }
		}

		public BinaryEditorDocumentSession ImportJson(ImportedJsonDocument p_imported)
		{
			if (p_imported == null) throw new ArgumentNullException(nameof(p_imported));
			string path = CreateTemporaryPath(p_imported.Format);
			try
			{
				FormatInspection.WriteRegistered(p_imported.Format, p_imported.Model, path);
				BinaryEditorDocumentSession session = Open(path, p_imported.FileName);
				if (!session.CanWrite) throw new InvalidDataException(session.Diagnostic ?? "The imported document did not pass canonical LibLR1 validation.");
				return session;
			}
			finally { TryDelete(path); }
		}

		public static BinaryDiffSummary Compare(byte[] p_source, byte[] p_candidate)
		{
			p_source ??= Array.Empty<byte>();
			p_candidate ??= Array.Empty<byte>();
			int shared = Math.Min(p_source.Length, p_candidate.Length);
			long? firstDifference = null;
			for (int i = 0; i < shared; i++)
			{
				if (p_source[i] != p_candidate[i]) { firstDifference = i; break; }
			}
			if (firstDifference == null && p_source.Length != p_candidate.Length) firstDifference = shared;
			return new BinaryDiffSummary { SourceLength = p_source.Length, CandidateLength = p_candidate.Length, FirstDifferenceOffset = firstDifference };
		}

		private static BinaryEditorDocumentSession CreateSession(string p_format, string p_fileName, BinaryEditorEncoding p_encoding, FormatInspectionResult<object> p_inspection)
		{
			BinaryEditorDocumentSession session = new BinaryEditorDocumentSession
			{
				Format = p_format,
				FileName = p_fileName,
				Encoding = p_encoding,
				Inspection = p_inspection,
				SourceData = p_inspection.SourceData,
				DecompressedData = p_inspection.DecompressedData,
				EvidenceStatus = BinaryEditorMetadataCatalog.GetEvidenceStatus(p_format)
			};

			if (p_encoding == BinaryEditorEncoding.Tokenized)
			{
				try
				{
					if (p_inspection.DecompressedData == null) throw new InvalidDataException("LibLR1 could not produce a safely decompressed token stream.");
					(session.Text, session.OffsetMap) = ProjectTokens(p_inspection.DecompressedData, p_format);
					session.State = p_inspection.CanWrite ? BinaryEditorDocumentState.ValidSemantic : BinaryEditorDocumentState.InspectionOnly;
				}
				catch (Exception exception)
				{
					session.Encoding = BinaryEditorEncoding.Unregistered;
					session.Text = ProjectHex(p_inspection.SourceData);
					session.State = BinaryEditorDocumentState.InspectionOnly;
					session.CompileException = exception;
				}
			}
			else
			{
				session.Text = ProjectHex(p_inspection.SourceData);
				session.State = p_inspection.ParseSucceeded ? BinaryEditorDocumentState.RawOpaque : BinaryEditorDocumentState.InspectionOnly;
			}
			return session;
		}

		private static BinaryEditorDocumentSession CreateOpaque(string p_format, string p_fileName, byte[] p_source, Exception p_error)
		{
			return new BinaryEditorDocumentSession
			{
				Format = p_format,
				FileName = p_fileName,
				Encoding = BinaryEditorEncoding.Unregistered,
				State = BinaryEditorDocumentState.RawOpaque,
				SourceData = p_source,
				Text = ProjectHex(p_source),
				CompileException = p_error
			};
		}

		private static (string Text, TokenOffsetMap Map) ProjectTokens(byte[] p_data, string p_format)
		{
			StringBuilder buffer = new StringBuilder();
			TokenOffsetMap map = new TokenOffsetMap();
			int indent = 0;
			int bracketStack = 0;
			int bracketCount = -1;
			string pendingKeywordInfo = null;
			using (MemoryStream stream = new MemoryStream(p_data ?? Array.Empty<byte>(), false))
			using (LRBinaryReader reader = new LRBinaryReader(stream, false))
			{
				while (stream.Position < stream.Length)
				{
					long binaryOffset = stream.Position;
					int textOffset = buffer.Length;
					Token token = reader.ReadToken();
					map.Add(binaryOffset, textOffset);
					Util.RecursiveAppend(reader, token, ref buffer, ref indent, ref bracketStack, ref bracketCount, ref pendingKeywordInfo, p_format);
				}
			}
			return (buffer.ToString().Trim(), map);
		}

		private static string ProjectHex(byte[] p_data)
		{
			p_data ??= Array.Empty<byte>();
			StringBuilder output = new StringBuilder();
			for (int offset = 0; offset < p_data.Length; offset += 16)
			{
				output.Append(offset.ToString("X8")).Append("  ");
				int count = Math.Min(16, p_data.Length - offset);
				for (int i = 0; i < 16; i++) output.Append(i < count ? p_data[offset + i].ToString("X2") : "  ").Append(i == 7 ? "  " : " ");
				output.Append(" |");
				for (int i = 0; i < count; i++)
				{
					byte value = p_data[offset + i];
					output.Append(value >= 0x20 && value <= 0x7E ? (char)value : '.');
				}
				output.AppendLine("|");
			}
			return output.ToString().TrimEnd();
		}

		private static byte[] CompileTokenText(string p_text)
		{
			using MemoryStream compiled = Util.Compile(p_text);
			return compiled.ToArray();
		}

		private static byte[] CompileHex(string p_text)
		{
			List<byte> bytes = new List<byte>();
			foreach (string sourceLine in (p_text ?? string.Empty).Split('\n'))
			{
				string line = sourceLine.TrimEnd('\r');
				int separator = line.IndexOf("  ", StringComparison.Ordinal);
				int ascii = line.IndexOf('|');
				if (separator < 0 || ascii < separator) continue;
				string hex = line.Substring(separator + 2, ascii - separator - 2).Replace("  ", " ");
				foreach (string value in hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
				{
					if (value.Length == 2 && byte.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out byte parsed)) bytes.Add(parsed);
				}
			}
			return bytes.ToArray();
		}

		private static string NormalizeFormat(string p_format) => string.IsNullOrWhiteSpace(p_format) ? string.Empty : p_format.Trim().TrimStart('.').ToUpperInvariant();
		private static string CreateTemporaryPath(string p_format)
		{
			string directory = Path.Combine(Path.GetTempPath(), "LR1BinaryEditor", "validation");
			Directory.CreateDirectory(directory);
			return Path.Combine(directory, Guid.NewGuid().ToString("N") + "." + NormalizeFormat(p_format));
		}
		private static void TryDelete(string p_path) { try { if (File.Exists(p_path)) File.Delete(p_path); } catch { } }
	}

	internal static class BinaryEditorMetadataCatalog
	{
		private static readonly Dictionary<string, EditorSchemaDefinition> s_schemas = BuildSchemas();
		private static readonly Dictionary<string, string> s_evidence = LoadEvidence();

		public static string GetEvidenceStatus(string p_format) => s_evidence.TryGetValue(p_format ?? string.Empty, out string value) ? value : "UNRESOLVED";

		public static string GetTokenInfo(string p_format, Token p_token, bool p_isBlock)
		{
			if (!s_schemas.TryGetValue(p_format ?? string.Empty, out EditorSchemaDefinition schema)) return string.Empty;
			string tokenId = "0x" + ((byte)p_token).ToString("X2");
			EditorSchemaField field = FindField(schema.Fields, tokenId, p_isBlock ? schema.RootBlockId : null);
			if (field == null) return string.Empty;
			return string.IsNullOrWhiteSpace(field.Help) ? field.Label ?? field.Name : (field.Label ?? field.Name) + " — " + field.Help;
		}

		private static EditorSchemaField FindField(IEnumerable<EditorSchemaField> p_fields, string p_tokenId, string p_rootBlockId)
		{
			if (p_fields == null) return null;
			foreach (EditorSchemaField field in p_fields)
			{
				if (string.Equals(field.TokenId, p_tokenId, StringComparison.OrdinalIgnoreCase) && (p_rootBlockId == null || string.Equals(field.TokenId, p_rootBlockId, StringComparison.OrdinalIgnoreCase))) return field;
				EditorSchemaField nested = FindField(field.Fields, p_tokenId, null);
				if (nested != null && p_rootBlockId == null) return nested;
			}
			return null;
		}

		private static Dictionary<string, EditorSchemaDefinition> BuildSchemas()
		{
			string annotations = SchemaExporter.FindAnnotationDirectory(AppContext.BaseDirectory) ?? SchemaExporter.FindAnnotationDirectory(Directory.GetCurrentDirectory());
			SchemaExporter exporter = new SchemaExporter(annotations);
			return exporter.BuildAllDefinitions().ToDictionary(p_definition => p_definition.Format, StringComparer.OrdinalIgnoreCase);
		}

		private static Dictionary<string, string> LoadEvidence()
		{
			Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			string path = FindUpwards(AppContext.BaseDirectory, Path.Combine("docs", "reconciliation", "format-evidence.json"))
				?? FindUpwards(Directory.GetCurrentDirectory(), Path.Combine("LibLR1", "docs", "reconciliation", "format-evidence.json"))
				?? Path.Combine(AppContext.BaseDirectory, "format-evidence.json");
			if (!File.Exists(path)) return result;
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
			foreach (JsonElement item in document.RootElement.EnumerateArray())
			{
				if (item.TryGetProperty("Format", out JsonElement format) && item.TryGetProperty("EvidenceStatus", out JsonElement status)) result[format.GetString()] = status.GetString();
			}
			return result;
		}

		private static string FindUpwards(string p_start, string p_relative)
		{
			DirectoryInfo directory = new DirectoryInfo(Path.GetFullPath(p_start));
			while (directory != null)
			{
				string candidate = Path.Combine(directory.FullName, p_relative);
				if (File.Exists(candidate)) return candidate;
				directory = directory.Parent;
			}
			return null;
		}
	}
}
