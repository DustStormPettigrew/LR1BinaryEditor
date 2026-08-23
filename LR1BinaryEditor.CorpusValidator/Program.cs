using LibLR1;
using LR1BinaryEditor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length != 2)
{
	Console.Error.WriteLine("Usage: LR1BinaryEditor.CorpusValidator <format-corpus-matrix.json> <output-directory>");
	return 2;
}

string matrixPath = Path.GetFullPath(args[0]);
string outputDirectory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(outputDirectory);
Util.LoadKeywordInfo(AppContext.BaseDirectory);
BinaryEditorDocumentService service = new BinaryEditorDocumentService();
ValidationReport report = new ValidationReport { GeneratedUtc = DateTime.UtcNow, MatrixPath = matrixPath };
using JsonDocument matrix = JsonDocument.Parse(File.ReadAllText(matrixPath));
foreach (JsonElement corpusElement in matrix.RootElement.GetProperty("Corpora").EnumerateArray())
{
	string identity = corpusElement.GetProperty("Identity").GetString();
	string root = corpusElement.GetProperty("Root").GetString();
	if (root.StartsWith("extracted-from:", StringComparison.OrdinalIgnoreCase))
	{
		string archivePath = root.Substring("extracted-from:".Length);
		if (!File.Exists(archivePath)) continue;
		string scratch = Path.Combine(Path.GetTempPath(), "LR1BinaryEditor", "corpus", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(scratch);
		byte[] before = SHA256.HashData(File.ReadAllBytes(archivePath));
		try
		{
			new JAM(archivePath).Extract(scratch, true);
			ValidateCorpus(service, report, identity, root, scratch, true);
		}
		finally { Directory.Delete(scratch, true); }
		byte[] after = SHA256.HashData(File.ReadAllBytes(archivePath));
		if (!before.SequenceEqual(after)) throw new InvalidDataException("Reference archive changed during validation: " + archivePath);
	}
	else if (Directory.Exists(root))
	{
		ValidateCorpus(service, report, identity, root, root, false);
	}
}

report.Total = Sum(report.Corpora.SelectMany(corpus => corpus.Formats));
JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(Path.Combine(outputDirectory, "binary-editor-corpus-validation.json"), JsonSerializer.Serialize(report, options) + Environment.NewLine);
File.WriteAllText(Path.Combine(outputDirectory, "binary-editor-corpus-validation.md"), RenderMarkdown(report));
Console.WriteLine($"Binary Editor corpus validation: discovered={report.Total.Discovered} projected={report.Total.Projected} validated={report.Total.Validated} writable={report.Total.Writable} failed={report.Total.Failed} source-unchanged={report.Total.SourceUnchanged}");
return report.Total.Failed == 0 ? 0 : 1;

static void ValidateCorpus(BinaryEditorDocumentService p_service, ValidationReport p_report, string p_identity, string p_reportRoot, string p_scanRoot, bool p_scratchExtraction)
{
	CorpusResult corpus = new CorpusResult { Identity = p_identity, Root = p_reportRoot, ScratchExtraction = p_scratchExtraction };
	Dictionary<string, FormatTotals> totals = p_service.RegisteredFormats.ToDictionary(format => format, format => new FormatTotals { Format = format }, StringComparer.OrdinalIgnoreCase);
	foreach (string path in Directory.EnumerateFiles(p_scanRoot, "*", SearchOption.AllDirectories))
	{
		string format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
		if (!totals.TryGetValue(format, out FormatTotals value)) continue;
		value.Discovered++;
		byte[] sourceBefore = File.ReadAllBytes(path);
		try
		{
			BinaryEditorDocumentSession session = p_service.Open(path);
			value.Projected++;
			if (session.Encoding != BinaryEditorEncoding.Unregistered)
			{
				p_service.Validate(session, session.Text);
				value.Validated++;
			}
			if (session.CanWrite) value.Writable++;
			if (session.SourceDiff?.Identical == true) value.SourceIdentical++;
			if (session.DecompressedDiff?.Identical == true) value.ExpandedIdentical++;
			if (session.Encoding == BinaryEditorEncoding.RawOpaque && session.SourceDiff?.Identical == true) value.OpaqueIdentical++;
			if (!session.CanWrite)
			{
				value.Failed++;
				corpus.Failures.Add(new FileFailure
				{
					Path = Path.GetRelativePath(p_scanRoot, path), Format = format, Stage = session.CompileException != null ? "compile" : "inspect",
					Issue = session.Issue?.Kind.ToString(), Token = session.Issue?.TokenId.HasValue == true ? "0x" + session.Issue.TokenId.Value.ToString("X2") : null,
					Offset = session.Issue?.DecompressedOffset, Message = session.Diagnostic
				});
			}
		}
		catch (Exception exception)
		{
			value.Failed++;
			corpus.Failures.Add(new FileFailure { Path = Path.GetRelativePath(p_scanRoot, path), Format = format, Stage = "project", Message = exception.Message });
		}
		byte[] sourceAfter = File.ReadAllBytes(path);
		if (!sourceBefore.SequenceEqual(sourceAfter)) throw new InvalidDataException("Corpus source changed during validation: " + path);
		value.SourceUnchanged++;
	}
	corpus.Formats = totals.Values.Where(value => value.Discovered != 0).OrderBy(value => value.Format).ToList();
	corpus.Total = Sum(corpus.Formats);
	p_report.Corpora.Add(corpus);
	Console.WriteLine($"{p_identity}: discovered={corpus.Total.Discovered} validated={corpus.Total.Validated} writable={corpus.Total.Writable} failed={corpus.Total.Failed}");
}

static FormatTotals Sum(IEnumerable<FormatTotals> p_values)
{
	FormatTotals total = new FormatTotals { Format = "TOTAL" };
	foreach (FormatTotals value in p_values)
	{
		total.Discovered += value.Discovered; total.Projected += value.Projected; total.Validated += value.Validated; total.Writable += value.Writable;
		total.SourceIdentical += value.SourceIdentical; total.ExpandedIdentical += value.ExpandedIdentical; total.OpaqueIdentical += value.OpaqueIdentical;
		total.SourceUnchanged += value.SourceUnchanged; total.Failed += value.Failed;
	}
	return total;
}

static string RenderMarkdown(ValidationReport p_report)
{
	StringBuilder text = new StringBuilder();
	text.AppendLine("# LR1 Binary Editor corpus validation").AppendLine();
	text.AppendLine("Generated UTC: " + p_report.GeneratedUtc.ToString("O")).AppendLine();
	text.AppendLine("All candidates were compiled/written below the system temporary directory. Source files and source JAM archives were hash/byte checked after validation.").AppendLine();
	text.AppendLine("| Corpus | Discovered | Projected | Validated | Writable | Source-identical | Expanded-identical | Opaque-identical | Failed | Source unchanged |");
	text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
	foreach (CorpusResult corpus in p_report.Corpora) text.AppendLine($"| {corpus.Identity} | {corpus.Total.Discovered} | {corpus.Total.Projected} | {corpus.Total.Validated} | {corpus.Total.Writable} | {corpus.Total.SourceIdentical} | {corpus.Total.ExpandedIdentical} | {corpus.Total.OpaqueIdentical} | {corpus.Total.Failed} | {corpus.Total.SourceUnchanged} |");
	text.AppendLine($"| **TOTAL** | **{p_report.Total.Discovered}** | **{p_report.Total.Projected}** | **{p_report.Total.Validated}** | **{p_report.Total.Writable}** | **{p_report.Total.SourceIdentical}** | **{p_report.Total.ExpandedIdentical}** | **{p_report.Total.OpaqueIdentical}** | **{p_report.Total.Failed}** | **{p_report.Total.SourceUnchanged}** |").AppendLine();
	foreach (CorpusResult corpus in p_report.Corpora.Where(corpus => corpus.Failures.Count != 0))
	{
		text.AppendLine("## " + corpus.Identity + " failures").AppendLine();
		foreach (FileFailure failure in corpus.Failures) text.AppendLine($"- `{failure.Path}` ({failure.Format}, {failure.Stage}, {failure.Token ?? "no token"}, offset {failure.Offset?.ToString() ?? "n/a"}): {failure.Message}");
		text.AppendLine();
	}
	return text.ToString();
}

internal sealed class ValidationReport { public DateTime GeneratedUtc { get; set; } public string MatrixPath { get; set; } public List<CorpusResult> Corpora { get; set; } = new(); public FormatTotals Total { get; set; } }
internal sealed class CorpusResult { public string Identity { get; set; } public string Root { get; set; } public bool ScratchExtraction { get; set; } public List<FormatTotals> Formats { get; set; } = new(); public List<FileFailure> Failures { get; set; } = new(); public FormatTotals Total { get; set; } }
internal sealed class FormatTotals { public string Format { get; set; } public int Discovered { get; set; } public int Projected { get; set; } public int Validated { get; set; } public int Writable { get; set; } public int SourceIdentical { get; set; } public int ExpandedIdentical { get; set; } public int OpaqueIdentical { get; set; } public int SourceUnchanged { get; set; } public int Failed { get; set; } }
internal sealed class FileFailure { public string Path { get; set; } public string Format { get; set; } public string Stage { get; set; } public string Issue { get; set; } public string Token { get; set; } public long? Offset { get; set; } public string Message { get; set; } }
