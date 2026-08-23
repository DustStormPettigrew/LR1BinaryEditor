using LibLR1.IO;
using LibLR1.Schema;
using LibLR1.Utils;
using LR1BinaryEditor;
using System.Text;

internal static class Program
{
	private static int s_checks;

	private static int Main()
	{
		string directory = Path.Combine(Path.GetTempPath(), "lr1-binary-editor-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			Util.LoadKeywordInfo(AppContext.BaseDirectory);
			Run(directory);
			Console.WriteLine($"LR1 Binary Editor headless checks passed: {s_checks}");
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
		finally { Directory.Delete(directory, true); }
	}

	private static void Run(string p_directory)
	{
		BinaryEditorDocumentService service = new BinaryEditorDocumentService();
		string valid = CreateValidCdb(p_directory, "valid.CDB");
		BinaryEditorDocumentSession document = service.Open(valid);
		Check(document.State == BinaryEditorDocumentState.ValidSemantic && document.CanWrite, "valid registered tokenized file");
		Check(document.SourceData.SequenceEqual(File.ReadAllBytes(valid)) && document.DecompressedData.Length > 0, "source and expanded bytes retained");
		string originalText = document.Text;
		service.Validate(document, originalText);
		Check(document.CanWrite && document.DecompressedDiff.Identical, "deterministic binary-text-binary projection");
		string output = Path.Combine(p_directory, "valid-output.CDB");
		service.Write(document, originalText, output);
		Check(File.ReadAllBytes(valid).SequenceEqual(File.ReadAllBytes(output)), "unchanged valid write is byte-identical");

		string compressed = CreateCompressedCdb(p_directory);
		BinaryEditorDocumentSession compressedDocument = service.Open(compressed);
		Check(compressedDocument.CanWrite && !compressedDocument.SourceData.SequenceEqual(compressedDocument.DecompressedData), "compressed tokenized input classified and expanded");
		service.Validate(compressedDocument, compressedDocument.Text);
		Check(compressedDocument.DecompressedDiff.Identical, "compressed input compares expanded token bytes");

		string unknownBlock = Path.Combine(p_directory, "unknown-block.CDB");
		File.WriteAllBytes(unknownBlock, new byte[] { 0x40 });
		BinaryEditorDocumentSession block = service.Open(unknownBlock);
		Check(block.State == BinaryEditorDocumentState.InspectionOnly && !block.CanWrite, "unrecognized block retained as inspection-only");
		Check(block.Issue?.Kind == LibLR1.Inspection.FormatInspectionIssueKind.UnrecognizedBlock && block.Issue.TokenId == 0x40 && block.Issue.DecompressedOffset == 0, "block token and offset reported");
		string malformedCompressed = Path.Combine(p_directory, "malformed-compressed.CDB");
		byte[] malformedBytes = { (byte)Token.Extended, 0x00, 0x01 };
		File.WriteAllBytes(malformedCompressed, malformedBytes);
		BinaryEditorDocumentSession malformed = service.Open(malformedCompressed);
		Check(malformed.State == BinaryEditorDocumentState.InspectionOnly && malformed.Encoding == BinaryEditorEncoding.Unregistered && malformed.SourceData.SequenceEqual(malformedBytes), "decompression failure remains open as exact source hex");

		string unknownProperty = CreateUnknownPropertyAdb(p_directory);
		BinaryEditorDocumentSession property = service.Open(unknownProperty);
		Check(property.Issue?.Kind == LibLR1.Inspection.FormatInspectionIssueKind.UnrecognizedProperty && property.Issue.TokenId == 0x40, "unrecognized property reported");
		Check(property.SourceData.SequenceEqual(File.ReadAllBytes(unknownProperty)) && property.DecompressedData.SequenceEqual(File.ReadAllBytes(unknownProperty)), "unknown property source and expanded bytes exact");
		string recovered = string.Join("\n", property.Text.Split('\n').Where(line => !line.Contains("0x40", StringComparison.OrdinalIgnoreCase)));
		service.Validate(property, recovered);
		Check(property.State == BinaryEditorDocumentState.ValidSemantic && property.CanWrite, "bad token removal reparses and restores writer");

		string corruptBmp = Path.Combine(p_directory, "corrupt.BMP");
		byte[] corrupt = { 1, 2, 3, 4 };
		File.WriteAllBytes(corruptBmp, corrupt);
		BinaryEditorDocumentSession rawFailure = service.Open(corruptBmp);
		Check(rawFailure.Encoding == BinaryEditorEncoding.RawOpaque && rawFailure.State == BinaryEditorDocumentState.InspectionOnly && rawFailure.SourceData.SequenceEqual(corrupt), "generic non-tokenized failure retained exactly");
		Check(rawFailure.CanEditRawHex && !rawFailure.CanEditTokenText, "raw failure never routes through token compiler");

		string sbk = Path.Combine(p_directory, "valid.SBK");
		File.WriteAllText(sbk, "SOUNDS\\\r\n1\r\nfoo.wav\r\n", Encoding.ASCII);
		BinaryEditorDocumentSession opaque = service.Open(sbk);
		Check(opaque.State == BinaryEditorDocumentState.RawOpaque && opaque.CanWrite && !opaque.CanEditText, "RawOpaque exact read-only state");
		string sbkCopy = Path.Combine(p_directory, "valid-copy.SBK");
		service.Write(opaque, opaque.Text, sbkCopy);
		Check(File.ReadAllBytes(sbk).SequenceEqual(File.ReadAllBytes(sbkCopy)), "RawOpaque write preserves exact bytes");

		Check(LibLR1JsonBridge.TryExportJson("CDB", document.FileName, document.Inspection.Document, document.CandidateData ?? document.SourceData, out string json, out string jsonError), "validated JSON export: " + jsonError);
		Check(LibLR1JsonBridge.TryImportJson(json, out ImportedJsonDocument imported, out jsonError), "JSON import projection: " + jsonError);
		BinaryEditorDocumentSession importedDocument = service.ImportJson(imported);
		Check(importedDocument.CanWrite && importedDocument.State == BinaryEditorDocumentState.ValidSemantic, "JSON import passes canonical temporary validation");

		BinaryDiffSummary diff = BinaryEditorDocumentService.Compare(new byte[] { 1, 2, 3 }, new byte[] { 1, 4, 3, 5 });
		Check(diff.SourceLength == 3 && diff.CandidateLength == 4 && diff.FirstDifferenceOffset == 1, "before-after binary diff");

		string jamExtracted = Path.Combine(p_directory, "jam", "GAMEDATA", "ENTRY.CDB");
		Directory.CreateDirectory(Path.GetDirectoryName(jamExtracted));
		File.Copy(valid, jamExtracted);
		Check(service.Open(jamExtracted).CanWrite, "JAM extracted entry uses the same inspection route");

		Check(service.RegisteredFormats.SequenceEqual(SchemaStructureProvider.Formats), "all registered formats route through canonical registry");
		Check(Util.FormatDescriptions.Keys.All(key => service.RegisteredFormats.Contains(key, StringComparer.OrdinalIgnoreCase)), "presentation descriptions do not create semantic formats");
	}

	private static string CreateValidCdb(string p_directory, string p_name)
	{
		string path = Path.Combine(p_directory, p_name);
		using LRBinaryWriter writer = new LRBinaryWriter(File.Create(path));
		writer.WriteByte(0x28);
		writer.WriteStringArrayBlock(new[] { "world" });
		return path;
	}

	private static string CreateCompressedCdb(string p_directory)
	{
		string path = Path.Combine(p_directory, "compressed.CDB");
		using LRBinaryWriter writer = new LRBinaryWriter(File.Create(path));
		writer.WriteByte(0x28);
		writer.WriteToken(Token.Struct);
		writer.WriteByte(0x17);
		writer.WriteByte(6);
		writer.WriteToken(Token.LeftBracket);
		writer.WriteToken(Token.Int32);
		writer.WriteToken(Token.RightBracket);
		writer.WriteToken(Token.LeftCurly);
		writer.WriteToken(Token.String);
		writer.WriteToken(Token.RightCurly);
		writer.WriteByte(0x17);
		writer.WriteInt(1);
		writer.WriteString("world");
		return path;
	}

	private static string CreateUnknownPropertyAdb(string p_directory)
	{
		string path = Path.Combine(p_directory, "unknown-property.ADB");
		using LRBinaryWriter writer = new LRBinaryWriter(File.Create(path));
		writer.WriteByte(0x27);
		writer.WriteToken(Token.LeftCurly);
		writer.WriteByte(0x40);
		writer.WriteToken(Token.RightCurly);
		return path;
	}

	private static void Check(bool p_condition, string p_name)
	{
		if (!p_condition) throw new InvalidDataException("Check failed: " + p_name);
		s_checks++;
	}
}
