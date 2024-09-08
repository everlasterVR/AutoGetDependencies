using System.Collections.Generic;
using System.Text;
using UnityEngine;

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
