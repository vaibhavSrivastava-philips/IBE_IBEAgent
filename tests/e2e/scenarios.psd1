#
# scenarios.psd1 - the end-to-end scenario matrix.
#
# Each scenario exercises one input transport, one acknowledgement mode, and one
# or more output transports. The workflow generates a contract for each entry,
# drives a message through it, and asserts delivery plus the source-side reply.
#
# Fields:
#   Name    - human-readable identifier shown in logs and the summary report.
#   Input   - inbound transport: 'tcp', 'http', or 'websocket'.
#   Ack     - reply mode: 'none' | 'normal' | 'enhanced' | 'response'.
#   Outputs - one or more outbound transports: 'tcp', 'http', and/or 'websocket'.
#
# Reply-mode meaning:
#   none      No acknowledgement is configured. TCP sources receive nothing;
#             HTTP sources are released with 504 after the reply timeout. The
#             message is still delivered downstream.
#   normal    A fixed "received" acknowledgement is returned as soon as the
#             message is accepted, independent of downstream delivery.
#   enhanced  The acknowledgement reflects the real downstream delivery outcome
#             and is returned only after the required leg settles.
#   response  The downstream system's own response payload is relayed back to
#             the source. Requires exactly one output leg.
#
@{
    Scenarios = @(
    # ---- TCP input --------------------------------------------------------
    @{ Name = 'TCP in, TCP out, no ack';        Input = 'tcp';  Ack = 'none';     Outputs = @('tcp') }
    @{ Name = 'TCP in, TCP out, normal ack';    Input = 'tcp';  Ack = 'normal';   Outputs = @('tcp') }
    @{ Name = 'TCP in, TCP out, enhanced ack';  Input = 'tcp';  Ack = 'enhanced'; Outputs = @('tcp') }
    @{ Name = 'TCP in, TCP out, response';      Input = 'tcp';  Ack = 'response'; Outputs = @('tcp') }
    @{ Name = 'TCP in, HTTP out, no ack';       Input = 'tcp';  Ack = 'none';     Outputs = @('http') }
    @{ Name = 'TCP in, HTTP out, normal ack';   Input = 'tcp';  Ack = 'normal';   Outputs = @('http') }
    @{ Name = 'TCP in, HTTP out, enhanced ack'; Input = 'tcp';  Ack = 'enhanced'; Outputs = @('http') }
    @{ Name = 'TCP in, HTTP out, response';     Input = 'tcp';  Ack = 'response'; Outputs = @('http') }

    # ---- HTTP input -------------------------------------------------------
    @{ Name = 'HTTP in, TCP out, no ack';       Input = 'http'; Ack = 'none';     Outputs = @('tcp') }
    @{ Name = 'HTTP in, TCP out, normal ack';   Input = 'http'; Ack = 'normal';   Outputs = @('tcp') }
    @{ Name = 'HTTP in, TCP out, enhanced ack'; Input = 'http'; Ack = 'enhanced'; Outputs = @('tcp') }
    @{ Name = 'HTTP in, TCP out, response';     Input = 'http'; Ack = 'response'; Outputs = @('tcp') }
    @{ Name = 'HTTP in, HTTP out, no ack';      Input = 'http'; Ack = 'none';     Outputs = @('http') }
    @{ Name = 'HTTP in, HTTP out, normal ack';  Input = 'http'; Ack = 'normal';   Outputs = @('http') }
    @{ Name = 'HTTP in, HTTP out, enhanced ack';Input = 'http'; Ack = 'enhanced'; Outputs = @('http') }
    @{ Name = 'HTTP in, HTTP out, response';    Input = 'http'; Ack = 'response'; Outputs = @('http') }

    # ---- WebSocket input ----------------------------------------------------
    @{ Name = 'WebSocket in, TCP out, no ack';        Input = 'websocket'; Ack = 'none';     Outputs = @('tcp') }
    @{ Name = 'WebSocket in, TCP out, normal ack';     Input = 'websocket'; Ack = 'normal';   Outputs = @('tcp') }
    @{ Name = 'WebSocket in, TCP out, enhanced ack';   Input = 'websocket'; Ack = 'enhanced'; Outputs = @('tcp') }
    @{ Name = 'WebSocket in, TCP out, response';       Input = 'websocket'; Ack = 'response'; Outputs = @('tcp') }
    @{ Name = 'WebSocket in, HTTP out, no ack';        Input = 'websocket'; Ack = 'none';     Outputs = @('http') }
    @{ Name = 'WebSocket in, HTTP out, normal ack';    Input = 'websocket'; Ack = 'normal';   Outputs = @('http') }
    @{ Name = 'WebSocket in, HTTP out, enhanced ack';  Input = 'websocket'; Ack = 'enhanced'; Outputs = @('http') }
    @{ Name = 'WebSocket in, HTTP out, response';      Input = 'websocket'; Ack = 'response'; Outputs = @('http') }
    @{ Name = 'WebSocket in, WebSocket out, no ack';       Input = 'websocket'; Ack = 'none';     Outputs = @('websocket') }
    @{ Name = 'WebSocket in, WebSocket out, normal ack';   Input = 'websocket'; Ack = 'normal';   Outputs = @('websocket') }
    @{ Name = 'WebSocket in, WebSocket out, enhanced ack'; Input = 'websocket'; Ack = 'enhanced'; Outputs = @('websocket') }
    @{ Name = 'WebSocket in, WebSocket out, response';     Input = 'websocket'; Ack = 'response'; Outputs = @('websocket') }

    # ---- WebSocket output (from existing input transports) ------------------
    @{ Name = 'TCP in, WebSocket out, enhanced ack';  Input = 'tcp';  Ack = 'enhanced'; Outputs = @('websocket') }
    @{ Name = 'HTTP in, WebSocket out, enhanced ack'; Input = 'http'; Ack = 'enhanced'; Outputs = @('websocket') }

    # ---- Fan-out (one input, two outputs) ---------------------------------
    @{ Name = 'TCP in, fan-out TCP + HTTP, enhanced ack';  Input = 'tcp';  Ack = 'enhanced'; Outputs = @('tcp', 'http') }
    @{ Name = 'HTTP in, fan-out TCP + HTTP, normal ack';   Input = 'http'; Ack = 'normal';   Outputs = @('tcp', 'http') }
    @{ Name = 'WebSocket in, fan-out TCP + WebSocket, enhanced ack'; Input = 'websocket'; Ack = 'enhanced'; Outputs = @('tcp', 'websocket') }
)
}
