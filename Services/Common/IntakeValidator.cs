using System.Text.RegularExpressions;

namespace SengokuProvider.API.Services.Common
{
    internal class IntakeValidator
    {
        internal bool IsValidIdentifier(string input)
        {
            return IsValidIdentifier(new string[] { input });
        }
        internal bool IsValidIdentifier(string[] inputs)
        {
            var regex = new Regex("^[a-zA-Z0-9_@.-]+$", RegexOptions.Compiled);

            foreach (var input in inputs)
            {
                if (string.IsNullOrWhiteSpace(input) ||
                    input.IndexOfAny(new char[] { ';', '\'', '\"', '/', '*', '-', '+' }) >= 0 ||
                    ContainsDangerousSqlPatterns(input) ||
                    !regex.IsMatch(input))
                {
                    return false;
                }
            }

            return true;
        }
        private bool ContainsDangerousSqlPatterns(string input)
        {
            var lowerInput = input.ToLowerInvariant();
            if (lowerInput.Contains("--") ||
                lowerInput.Contains("/*") ||
                lowerInput.Contains("*/") ||
                lowerInput.Contains(" or ") ||
                lowerInput.Contains(" and ") ||
                lowerInput.Contains("exec(") ||
                lowerInput.Contains("execute(") ||
                lowerInput.Contains("drop ") ||
                lowerInput.Contains("insert ") ||
                lowerInput.Contains("delete from ") ||
                lowerInput.Contains("update ") ||
                lowerInput.Contains("<script>") ||
                lowerInput.Contains("</script>"))
            {
                return true;
            }

            return false;
        }
    }
}
