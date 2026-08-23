using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LR1BinaryEditor
{
    internal sealed class ListGroupSeparatorRenderer : IBackgroundRenderer
    {
        private static readonly Regex ms_countLine = new Regex(@"^\s*\[\s*(\d+)\s*\]\s*$", RegexOptions.Compiled);
        private static readonly Regex ms_openBraceLine = new Regex(@"^\s*\{\s*$", RegexOptions.Compiled);
        private static readonly Regex ms_closeBraceLine = new Regex(@"^\s*\}\s*$", RegexOptions.Compiled);
        private static readonly Regex ms_scalarLine = new Regex(@"^\s*(?:\((?:float|ushort|bool|byte|int|f8|f16)\))?(?:true|false|-?\d+(?:\.\d+)?)(?:\s*//.*)?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly HashSet<int> m_separatorAfterLines = new HashSet<int>();
        private readonly Pen m_pen = new Pen(new SolidColorBrush(Color.FromArgb(120, 130, 150, 175)), 1);

        public KnownLayer Layer => KnownLayer.Selection;

        public void UpdateText(string text)
        {
            m_separatorAfterLines.Clear();
            if (string.IsNullOrWhiteSpace(text))
                return;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                Match countMatch = ms_countLine.Match(lines[i]);
                if (!countMatch.Success || !int.TryParse(countMatch.Groups[1].Value, out int objectCount) || objectCount <= 0)
                    continue;

                int openLine = FindNextMeaningfulLine(lines, i + 1);
                if (openLine < 0 || !ms_openBraceLine.IsMatch(lines[openLine]))
                    continue;

                TryAddGroupSeparators(lines, openLine, objectCount);
            }
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (textView?.Document == null || m_separatorAfterLines.Count == 0)
                return;

            foreach (int lineNumber in m_separatorAfterLines)
            {
                if (lineNumber < 1 || lineNumber > textView.Document.LineCount)
                    continue;

                DocumentLine line = textView.Document.GetLineByNumber(lineNumber);
                foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
                {
                    double y = Math.Floor(rect.Bottom) + 0.5;
                    double startX = Math.Max(rect.Left, 0);
                    double endX = Math.Max(startX, textView.Bounds.Width - 12);
                    drawingContext.DrawLine(m_pen, new Point(startX, y), new Point(endX, y));
                    break;
                }
            }
        }

        private void TryAddGroupSeparators(string[] lines, int openLine, int objectCount)
        {
            int depth = 1;
            bool sawUnsupportedDirectContent = false;
            List<int> scalarLineNumbers = new List<int>();

            for (int i = openLine + 1; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();

                if (trimmed.Length == 0)
                    continue;

                if (ms_openBraceLine.IsMatch(line))
                {
                    depth++;
                    if (depth > 1)
                        sawUnsupportedDirectContent = true;
                    continue;
                }

                if (ms_closeBraceLine.IsMatch(line))
                {
                    depth--;
                    if (depth == 0)
                        break;
                    continue;
                }

                if (depth == 1)
                {
                    if (ms_scalarLine.IsMatch(line))
                        scalarLineNumbers.Add(i + 1);
                    else
                        sawUnsupportedDirectContent = true;
                }
            }

            if (sawUnsupportedDirectContent || scalarLineNumbers.Count <= objectCount)
                return;

            if (scalarLineNumbers.Count % objectCount != 0)
                return;

            int groupSize = scalarLineNumbers.Count / objectCount;
            if (groupSize < 2 || groupSize > 16)
                return;

            for (int i = groupSize - 1; i < scalarLineNumbers.Count - 1; i += groupSize)
                m_separatorAfterLines.Add(scalarLineNumbers[i]);
        }

        private static int FindNextMeaningfulLine(string[] lines, int start)
        {
            for (int i = start; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;
                return i;
            }

            return -1;
        }
    }
}
