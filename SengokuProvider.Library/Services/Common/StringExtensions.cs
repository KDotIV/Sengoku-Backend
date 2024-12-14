namespace SengokuProvider.Library.Services.Common
{
    static class StringExtensions
    {
        public static IEnumerable<String> SplitByNum(String s, Int32 partLength)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (partLength < 0) throw new ArgumentOutOfRangeException("Length must be positive.", nameof(partLength));
            for (int i = 0; i < s.Length; i += partLength)
                yield return s.Substring(i, Math.Min(partLength, s.Length - i));
        }
    }
}
