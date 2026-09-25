using System.Text;
using System.Xml;

namespace CrashReporterDotNET
{
    /// <summary>
    /// Helpers for putting arbitrary text (exception messages, user input) into XML.
    /// </summary>
    internal static class XmlText
    {
        private const char ReplacementCharacter = '�';

        /// <summary>
        /// Replaces unpaired surrogates, which cannot be encoded at all, with U+FFFD.
        /// Other characters that are invalid in XML (e.g. U+0001) are kept; the failed report writer escapes them
        /// as character references, so they round-trip exactly.
        /// </summary>
        public static string ReplaceLoneSurrogates(string text)
        {
            return Replace(text, false);
        }

        /// <summary>
        /// Replaces every character that is not allowed in an XML 1.0 document (control characters, U+FFFE, U+FFFF
        /// and unpaired surrogates) with U+FFFD, so the text can be sent to services that validate XML strictly.
        /// </summary>
        public static string ToValidXml(string text)
        {
            return Replace(text, true);
        }

        private static string Replace(string text, bool replaceInvalidXmlCharacters)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            StringBuilder builder = null;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                bool valid;
                if (char.IsHighSurrogate(c))
                {
                    valid = i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                    if (valid)
                    {
                        builder?.Append(c).Append(text[i + 1]);
                        i++;
                        continue;
                    }
                }
                else if (char.IsLowSurrogate(c))
                {
                    valid = false;
                }
                else
                {
                    valid = !replaceInvalidXmlCharacters || XmlConvert.IsXmlChar(c);
                }

                if (!valid && builder == null)
                {
                    builder = new StringBuilder(text.Length);
                    builder.Append(text, 0, i);
                }

                builder?.Append(valid ? c : ReplacementCharacter);
            }

            return builder?.ToString() ?? text;
        }
    }
}
