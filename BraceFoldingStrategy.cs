using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using System.Collections.Generic;

namespace LR1BinaryEditor
{
    sealed class BraceFoldingStrategy
    {
        public void UpdateFoldings(FoldingManager manager, TextDocument document)
        {
            var foldings = new List<NewFolding>();
            var openBraces = new Stack<int>();
            int offset = 0;
            string text = document.Text;

            foreach (char ch in text)
            {
                if (ch == '{')
                    openBraces.Push(offset);
                else if (ch == '}' && openBraces.Count > 0)
                {
                    int start = openBraces.Pop();
                    if (offset > start + 1)
                        foldings.Add(new NewFolding(start, offset + 1));
                }
                offset++;
            }

            foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
            int documentLength = document.TextLength;
            foldings.RemoveAll(f => f.StartOffset < 0 || f.EndOffset > documentLength || f.StartOffset >= f.EndOffset);
            manager.UpdateFoldings(foldings, -1);
        }
    }
}
