using AvaloniaEdit.Highlighting;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LR1BinaryEditor
{
    internal sealed class Lr1HighlightingDefinition : IHighlightingDefinition
    {
        private readonly HighlightingRuleSet m_mainRuleSet = new HighlightingRuleSet();
        private readonly Dictionary<string, HighlightingColor> m_colors = new Dictionary<string, HighlightingColor>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> m_properties = new Dictionary<string, string>();

        private Lr1HighlightingDefinition()
        {
            HighlightingColor token = AddColor("Token", "#7A3E9D");
            HighlightingColor datatype = AddColor("Datatype", "#0066AA");
            HighlightingColor text = AddColor("String", "#A31515");
            HighlightingColor objectCount = AddColor("Object Count", "#795E26");
            HighlightingColor number = AddColor("Number", "#098658");
            HighlightingColor braces = AddColor("Object Braces", "#333333");
            HighlightingColor comment = AddColor("Comment", "#008000");

            AddRule(@"(?<![\w])0x[0-9A-Fa-f]{2}(?![\w])", token);
            AddRule(@"\((?:float|ushort|bool|byte|int|f8|f16)\)", datatype);
            AddRule(@"""[^""\\\r\n]*(?:\\.[^""\\\r\n]*)*""", text);
            AddRule(@"\[\s*-?\d+\s*\]", objectCount);
            AddRule(@"(?<![\w.])-?\d+(?:\.\d+)?(?![\w.])", number);
            AddRule(@"[\{\}]", braces);
            AddRule(@"//.*$", comment);
        }

        public string Name => "LR1 Binary";
        public HighlightingRuleSet MainRuleSet => m_mainRuleSet;
        public IEnumerable<HighlightingColor> NamedHighlightingColors => m_colors.Values;
        public IDictionary<string, string> Properties => m_properties;

        public static Lr1HighlightingDefinition Create()
        {
            return new Lr1HighlightingDefinition();
        }

        public HighlightingRuleSet GetNamedRuleSet(string name)
        {
            return null;
        }

        public HighlightingColor GetNamedColor(string name)
        {
            return m_colors.TryGetValue(name, out HighlightingColor color) ? color : null;
        }

        private HighlightingColor AddColor(string name, string foreground)
        {
            HighlightingColor color = new HighlightingColor
            {
                Name = name,
                Foreground = new SimpleHighlightingBrush(Color.Parse(foreground))
            };
            m_colors[name] = color;
            return color;
        }

        private void AddRule(string regex, HighlightingColor color)
        {
            m_mainRuleSet.Rules.Add(new HighlightingRule
            {
                Regex = new Regex(regex, RegexOptions.Compiled),
                Color = color
            });
        }
    }
}
