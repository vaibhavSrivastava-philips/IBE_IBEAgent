using System.Text;

namespace IbePerf;

// Built-in HL7 v2 corpus so the tool works with zero external files. Messages are stamped with a
// per-message sequence id in MSH-10 (the agent's control id) for end-to-end correlation, and can be
// padded to a target size to simulate large ORU payloads.
internal static class Hl7Corpus
{
    private static readonly Dictionary<string, string[]> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ADT^A01"] = new[]
        {
            @"MSH|^~\&|IBEPERF|LOADGEN|IBEAGENT|FACILITY|20260807120000||ADT^A01|CTRLID|P|2.4",
            "EVN|A01|20260807120000",
            "PID|1||123456^^^HOSP^MR||DOE^JOHN^A||19700101|M|||123 MAIN ST^^METROPOLIS^NY^10001",
            "PV1|1|I|WARD^101^1|||||||MED||||ADM|A0",
        },
        ["ORU^R01"] = new[]
        {
            @"MSH|^~\&|IBEPERF|LOADGEN|IBEAGENT|FACILITY|20260807120000||ORU^R01|CTRLID|P|2.4",
            "PID|1||123456^^^HOSP^MR||DOE^JOHN^A||19700101|M",
            "OBR|1|ORDER123|FILLER456|CBC^Complete Blood Count",
            "OBX|1|NM|WBC^White Blood Cell||7.2|10*3/uL|4.0-11.0|N|||F",
        },
        ["ORM^O01"] = new[]
        {
            @"MSH|^~\&|IBEPERF|LOADGEN|IBEAGENT|FACILITY|20260807120000||ORM^O01|CTRLID|P|2.4",
            "PID|1||123456^^^HOSP^MR||DOE^JOHN^A||19700101|M",
            "ORC|NW|ORDER123",
            "OBR|1|ORDER123||CBC^Complete Blood Count",
        },
    };

    public static byte[] Build(string type, long seq, int sizeBytes)
    {
        if (!Templates.TryGetValue(type, out var segs)) segs = Templates["ADT^A01"];

        var msh = SetMsh10(segs[0], seq.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var sb = new StringBuilder(msh);
        for (var i = 1; i < segs.Length; i++) sb.Append('\r').Append(segs[i]);

        if (sizeBytes > 0)
        {
            var current = Encoding.UTF8.GetByteCount(sb.ToString());
            if (sizeBytes > current)
            {
                sb.Append("\rZPD|");
                sb.Append('X', sizeBytes - current - 5); // -5 ~ "\rZPD|"
            }
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string SetMsh10(string msh, string ctrlId)
    {
        var parts = msh.Split('|');
        if (parts.Length > 9) parts[9] = ctrlId; // MSH-10 = index 9 after split
        return string.Join('|', parts);
    }

    // Extracts the MSH-10 control id (the seq we stamped); -1 if not parseable.
    public static long ExtractSeq(ReadOnlySpan<byte> payload)
    {
        var cr = payload.IndexOf((byte)'\r');
        var first = cr >= 0 ? payload[..cr] : payload;
        var text = Encoding.UTF8.GetString(first);
        if (!text.StartsWith("MSH", StringComparison.Ordinal)) return -1;
        var parts = text.Split('|');
        return parts.Length > 9 && long.TryParse(parts[9], out var n) ? n : -1;
    }

    public static byte[] Msa(string code, long seq) =>
        Encoding.UTF8.GetBytes($"MSH|^~\\&|IBEAGENT|FACILITY|IBEPERF|SINK|20260807120000||ACK|{seq}|P|2.4\rMSA|{code}|{seq}");
}
