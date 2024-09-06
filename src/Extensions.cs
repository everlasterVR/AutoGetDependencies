using System.Text;

// Licensed under Creative Commons Attribution 4.0 International https://creativecommons.org/licenses/by/4.0/
// (c) 2024 everlaster
namespace everlaster
{
    static class StringBuilderExtensions
    {
        public static StringBuilder Clear(this StringBuilder sb)
        {
            sb.Length = 0;
            return sb;
        }
    }
}
