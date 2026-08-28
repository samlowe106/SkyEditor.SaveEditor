using System;
using System.Text;

namespace SkyEditor.SaveEditor
{
    /// <summary>
    /// Reads the "SharkPortSave" container format used by GameShark/Action Replay-era save
    /// exports (commonly distributed with a .sps extension, e.g. GameFAQs' save file downloads).
    /// </summary>
    /// <remarks>
    /// The format wraps the real battery-backed save data in a small text header (game title,
    /// export date, an optional notes field), but the exact byte layout of what follows those
    /// variable-length text fields differs enough between exports that hand-parsing every field
    /// isn't reliable. Instead, <see cref="ExtractPayload"/> locates the real save payload by
    /// scanning for the byte offset where a candidate-sized slice satisfies the wrapped format's
    /// own checksum -- a wrong offset passing that checksum by coincidence is astronomically
    /// unlikely, so this is effectively self-verifying regardless of header version quirks.
    /// </remarks>
    public static class SharkPortFile
    {
        private const string Magic = "SharkPortSave";

        /// <summary>True if the given bytes look like a SharkPortSave container (the magic string is at offset 4).</summary>
        public static bool IsSharkPortFormat(byte[] data)
        {
            var magicBytes = Encoding.ASCII.GetBytes(Magic);
            if (data.Length < 4 + magicBytes.Length)
            {
                return false;
            }

            for (int i = 0; i < magicBytes.Length; i++)
            {
                if (data[4 + i] != magicBytes[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Scans the container for a <paramref name="payloadLength"/>-byte slice that satisfies
        /// <paramref name="isValidPayload"/>, and returns the first one found, or null if none does.
        /// </summary>
        public static byte[] ExtractPayload(byte[] data, int payloadLength, Func<byte[], bool> isValidPayload)
        {
            var candidate = new byte[payloadLength];
            for (int offset = 0; offset + payloadLength <= data.Length; offset++)
            {
                Array.Copy(data, offset, candidate, 0, payloadLength);
                if (isValidPayload(candidate))
                {
                    return (byte[])candidate.Clone();
                }
            }

            return null;
        }
    }
}
