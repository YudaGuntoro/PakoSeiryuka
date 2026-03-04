using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PakoSeiryuka.Singletone
{
    public static class PLCDataFormat
    {
        /// <summary>
        /// Converts a byte array to a value of type T using the CDAB byte order.
        /// </summary>
        /// <typeparam name="T">The target data type (e.g., int, float, double).</typeparam>
        /// <param name="bytes">The byte array (must be at least 4 bytes for most types).</param>
        /// <returns>The converted value of type T.</returns>
        public static T ConvertFromCDAB<T>(byte[] bytes) where T : struct
        {
            if (bytes == null || bytes.Length < 4)
                throw new ArgumentException("The byte array must contain at least 4 bytes.");

            // Rearrange bytes to CDAB order
            byte[] cdabBytes = { bytes[1], bytes[0], bytes[3], bytes[2] };

            // Handle type conversion
            if (typeof(T) == typeof(int))
                return (T)(object)BitConverter.ToInt32(cdabBytes, 0);
            if (typeof(T) == typeof(float))
                return (T)(object)BitConverter.ToSingle(cdabBytes, 0);
            if (typeof(T) == typeof(double))
                return (T)(object)BitConverter.ToDouble(cdabBytes, 0);

            throw new NotSupportedException($"The type {typeof(T)} is not supported.");
        }
        /// <summary>
        /// Converts a value of type T to a byte array in the CDAB byte order.
        /// </summary>
        /// <typeparam name="T">The source data type (e.g., int, float, double).</typeparam>
        /// <param name="value">The value to convert.</param>
        /// <returns>The byte array in CDAB order.</returns>
        public static byte[] ConvertToCDAB<T>(T value) where T : struct
        {
            byte[] bytes;

            // Handle type conversion
            if (typeof(T) == typeof(int))
                bytes = BitConverter.GetBytes((int)(object)value);
            else if (typeof(T) == typeof(float))
                bytes = BitConverter.GetBytes((float)(object)value);
            else if (typeof(T) == typeof(double))
                bytes = BitConverter.GetBytes((double)(object)value);
            else
                throw new NotSupportedException($"The type {typeof(T)} is not supported.");

            // Rearrange bytes to CDAB order
            return new byte[] { bytes[1], bytes[0], bytes[3], bytes[2] };
        }
    }
}
