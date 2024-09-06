using System.Text;

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
