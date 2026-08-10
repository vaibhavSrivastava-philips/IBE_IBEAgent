#
# file-scenarios.psd1 - the File comm-point end-to-end scenario matrix.
#
# Each scenario wires a File inbound and/or outbound comm point (plus, for
# cross-transport cases, the existing TCP/HTTP peers) and drives one uniquely
# marked message through it. Because a File source has no reply channel, the
# source-side assertion is the DISPOSITION of the input file, not a reply.
#
# Fields:
#   Name        - identifier shown in logs and the summary report.
#   Input       - inbound transport: 'file' | 'tcp' | 'http'.
#   Output      - outbound transport: 'file' | 'tcp' | 'http'.
#   Ack         - reply mode: 'none' | 'normal' | 'enhanced'. (Response is
#                 degenerate for a File source and is not exercised here.)
#   Disposition - File input only: 'Move' (default) | 'Watermark'. Maps to the
#                 KeepOriginalFiles config knob (Watermark = KeepOriginalFiles true).
#   Dead        - route the TCP output at a closed port to force a delivery
#                 failure (used to prove the input file lands in error/).
#   Content     - 'plain' (HL7) | 'envelope' (base64 blob envelope, decoded by
#                 the blob-envelope-extract pipeline, output name from filename) |
#                 'base64' (base64 payload decoded by the output leg's base64 codec).
#
@{
    Scenarios = @(
        # ---- Relay and cross-transport --------------------------------------
        @{ Name = 'File in -> File out, no ack';        Input = 'file'; Output = 'file'; Ack = 'none' }
        @{ Name = 'File in -> File out, enhanced ack';  Input = 'file'; Output = 'file'; Ack = 'enhanced' }
        @{ Name = 'File in -> TCP out, enhanced ack';   Input = 'file'; Output = 'tcp';  Ack = 'enhanced' }
        @{ Name = 'File in -> HTTP out, normal ack';    Input = 'file'; Output = 'http'; Ack = 'normal' }
        @{ Name = 'TCP in -> File out, no ack';         Input = 'tcp';  Output = 'file'; Ack = 'none' }
        @{ Name = 'HTTP in -> File out, no ack';        Input = 'http'; Output = 'file'; Ack = 'none' }

        # ---- Disposition modes ---------------------------------------------
        @{ Name = 'File in (Move) -> processed on delivery';    Input = 'file'; Output = 'tcp';  Ack = 'enhanced'; Disposition = 'Move' }
        @{ Name = 'File in (Move) -> error on failed delivery'; Input = 'file'; Output = 'tcp';  Ack = 'enhanced'; Disposition = 'Move'; Dead = $true }
        @{ Name = 'File in (Watermark) -> file left in place';  Input = 'file'; Output = 'file'; Ack = 'none';     Disposition = 'Watermark' }

        # ---- Content (base64 codec + blob envelope) -------------------------
        @{ Name = 'File in -> File out, base64 blob envelope';  Input = 'file'; Output = 'file'; Ack = 'none'; Content = 'envelope' }
        @{ Name = 'File in -> File out, base64 payload codec';  Input = 'file'; Output = 'file'; Ack = 'none'; Content = 'base64' }
    )
}
