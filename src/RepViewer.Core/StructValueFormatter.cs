using System.Globalization;

namespace RepViewer.Core;

internal static class StructValueFormatter
{
    public static object FormatFixedBuffer(byte[] bytes, Type elementType)
    {
        if (elementType == typeof(byte)) return bytes;
        var width = System.Runtime.InteropServices.Marshal.SizeOf(elementType);
        if (width <= 0 || bytes.Length % width != 0) return bytes;
        var values = new object[bytes.Length / width];
        for (var index = 0; index < values.Length; index++)
        {
            var slice = bytes.AsSpan(index * width, width);
            values[index] = elementType == typeof(short) ? BitConverter.ToInt16(slice) :
                elementType == typeof(ushort) ? BitConverter.ToUInt16(slice) :
                elementType == typeof(int) ? BitConverter.ToInt32(slice) :
                elementType == typeof(uint) ? BitConverter.ToUInt32(slice) :
                elementType == typeof(float) ? BitConverter.ToSingle(slice) :
                Convert.ToHexString(slice);
        }
        return values;
    }
}
