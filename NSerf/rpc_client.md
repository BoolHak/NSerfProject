# RPC Client Implementation in Serf

The RPC client in the Serf agent codebase provides a mechanism for controlling Serf and streaming events over TCP using MsgPack encoding. Here's how it works:

## Connection Establishment

The RPC client connects to the Serf agent's RPC server through two primary factory functions:

**`NewRPCClient(addr string)`** - Creates a client with default settings by delegating to `ClientFromConfig`. [1](#2-0) 

**`ClientFromConfig(c *Config)`** - The main connection function that:
1. Establishes a TCP connection to the specified address with a timeout (default 10 seconds)
2. Creates buffered reader/writer and MsgPack encoder/decoder
3. Starts a background goroutine (`listen()`) to process incoming responses
4. Performs a handshake to establish protocol version
5. Optionally authenticates with an auth key if provided [2](#2-1) 

The `RPCClient` struct maintains the connection state, including the TCP connection, MsgPack codec, sequence numbers, and a dispatch map for matching responses to requests. [3](#2-2) 

## Protocol and Message Format

The protocol uses **request/response headers** with sequence numbers for matching:
- **Request Header**: Contains a command name and sequence number [4](#2-3) 
- **Response Header**: Contains a sequence number and optional error string [5](#2-4) 

Available commands include handshake, authentication, member operations (join, leave, members), events, streaming, monitoring, key management, queries, and statistics. [6](#2-5) 

## Sending Requests

Requests are sent using the **`send()` method**, which:
1. Acquires a write lock to serialize writes
2. Sets a write deadline using the configured timeout
3. Encodes the request header using MsgPack
4. Optionally encodes the request body (if provided)
5. Flushes the buffered writer [7](#2-6) 

Most RPC operations use the **`genericRPC()` pattern**, which:
1. Registers a callback handler for the request's sequence number
2. Sends the request using `send()`
3. Waits for the response on a channel or shutdown signal
4. Decodes the response body (if expected)
5. Returns any error from the response header [8](#2-7) 

Sequence numbers are atomically incremented to ensure unique identifiers for each request. [9](#2-8) 

## Receiving Responses

The **`listen()` goroutine** runs in the background and:
1. Continuously decodes response headers from the TCP connection
2. Dispatches each response to the registered handler based on sequence number
3. Closes the client when the connection terminates or an error occurs [10](#2-9) 

The **dispatch mechanism** uses a map (`dispatch`) that maps sequence numbers to handlers:
- **`handleSeq()`**: Registers a handler for a specific sequence number [11](#2-10) 
- **`respondSeq()`**: Invokes the handler when a response arrives [12](#2-11) 
- **`deregisterHandler()`**: Removes and cleans up a handler [13](#2-12) 

## Handler Types

The client supports different handler types for various operations:

1. **`seqCallback`**: Simple one-shot handlers for request/response operations [14](#2-13) 

2. **`monitorHandler`**: Handles continuous log streaming [15](#2-14) 

3. **`streamHandler`**: Handles continuous event streaming [16](#2-15) 

4. **`queryHandler`**: Handles query operations with acks and responses [17](#2-16) 

## Server-Side Processing

On the server side, the **`AgentIPC`** handles incoming connections:
1. Accepts TCP connections
2. Wraps each connection in an `IPCClient`
3. Spawns a goroutine to handle each client
4. Decodes request headers and dispatches to command-specific handlers [18](#2-17) 

The server validates handshakes and authentication before processing commands, and uses the same MsgPack encoding for consistency. [19](#2-18) 

## Notes

- The protocol uses **MsgPack** for efficient binary serialization with support for both old and new time formats
- **Thread safety** is maintained through multiple locks: `writeLock` for serializing writes, `dispatchLock` for handler map access, and `shutdownLock` for cleanup
- The default RPC port is **7373** (though this is configurable)
- All operations support **timeouts** to prevent indefinite hangs
- The client automatically handles connection cleanup on shutdown or errors
- The handshake establishes protocol version compatibility (currently version 1)

### Citations

**File:** client/rpc_client.go (L29-36)
```go
type seqCallback struct {
	handler func(*responseHeader)
}

func (sc *seqCallback) Handle(resp *responseHeader) {
	sc.handler(resp)
}
func (sc *seqCallback) Cleanup() {}
```

**File:** client/rpc_client.go (L64-84)
```go
// RPCClient is used to make requests to the Agent using an RPC mechanism.
// Additionally, the client manages event streams and monitors, enabling a client
// to easily receive event notifications instead of using the fork/exec mechanism.
type RPCClient struct {
	seq uint64

	timeout   time.Duration
	conn      *net.TCPConn
	reader    *bufio.Reader
	writer    *bufio.Writer
	dec       *codec.Decoder
	enc       *codec.Encoder
	writeLock sync.Mutex

	dispatch     map[uint64]seqHandler
	dispatchLock sync.Mutex

	shutdown     bool
	shutdownCh   chan struct{}
	shutdownLock sync.Mutex
}
```

**File:** client/rpc_client.go (L86-117)
```go
// send is used to send an object using the MsgPack encoding. send
// is serialized to prevent write overlaps, while properly buffering.
func (c *RPCClient) send(header *requestHeader, obj interface{}) error {
	c.writeLock.Lock()
	defer c.writeLock.Unlock()

	if c.shutdown {
		return clientClosed
	}

	// Setup an IO deadline, this way we won't wait indefinitely
	// if the client has hung.
	if err := c.conn.SetWriteDeadline(time.Now().Add(c.timeout)); err != nil {
		return err
	}

	if err := c.enc.Encode(header); err != nil {
		return err
	}

	if obj != nil {
		if err := c.enc.Encode(obj); err != nil {
			return err
		}
	}

	if err := c.writer.Flush(); err != nil {
		return err
	}

	return nil
}
```

**File:** client/rpc_client.go (L119-126)
```go
// NewRPCClient is used to create a new RPC client given the
// RPC address of the Serf agent. This will return a client,
// or an error if the connection could not be established.
// This will use the DefaultTimeout for the client.
func NewRPCClient(addr string) (*RPCClient, error) {
	conf := Config{Addr: addr}
	return ClientFromConfig(&conf)
}
```

**File:** client/rpc_client.go (L140-181)
```go
func ClientFromConfig(c *Config) (*RPCClient, error) {
	// Setup the defaults
	if c.Timeout == 0 {
		c.Timeout = DefaultTimeout
	}

	// Try to dial to serf
	conn, err := net.DialTimeout("tcp", c.Addr, c.Timeout)
	if err != nil {
		return nil, err
	}

	// Create the client
	client := &RPCClient{
		seq:        0,
		timeout:    c.Timeout,
		conn:       conn.(*net.TCPConn),
		reader:     bufio.NewReader(conn),
		writer:     bufio.NewWriter(conn),
		dispatch:   make(map[uint64]seqHandler),
		shutdownCh: make(chan struct{}),
	}
	client.dec = codec.NewDecoder(client.reader, c.newMsgpackHandle())
	client.enc = codec.NewEncoder(client.writer, c.newMsgpackHandle())
	go client.listen()

	// Do the initial handshake
	if err := client.handshake(); err != nil {
		client.Close()
		return nil, err
	}

	// Do the initial authentication if needed
	if c.AuthKey != "" {
		if err := client.auth(c.AuthKey); err != nil {
			client.Close()
			return nil, err
		}
	}

	return client, err
}
```

**File:** client/rpc_client.go (L421-463)
```go
type monitorHandler struct {
	client *RPCClient
	closed bool
	init   bool
	initCh chan<- error
	logCh  chan<- string
	seq    uint64
}

func (mh *monitorHandler) Handle(resp *responseHeader) {
	// Initialize on the first response
	if !mh.init {
		mh.init = true
		mh.initCh <- strToError(resp.Error)
		return
	}

	// Decode logs for all other responses
	var rec logRecord
	if err := mh.client.dec.Decode(&rec); err != nil {
		log.Printf("[ERR] Failed to decode log: %v", err)
		mh.client.deregisterHandler(mh.seq)
		return
	}
	select {
	case mh.logCh <- rec.Log:
	default:
		log.Printf("[ERR] Dropping log! Monitor channel full")
	}
}

func (mh *monitorHandler) Cleanup() {
	if !mh.closed {
		if !mh.init {
			mh.init = true
			mh.initCh <- errors.New("Stream closed")
		}
		if mh.logCh != nil {
			close(mh.logCh)
		}
		mh.closed = true
	}
}
```

**File:** client/rpc_client.go (L503-545)
```go
type streamHandler struct {
	client  *RPCClient
	closed  bool
	init    bool
	initCh  chan<- error
	eventCh chan<- map[string]interface{}
	seq     uint64
}

func (sh *streamHandler) Handle(resp *responseHeader) {
	// Initialize on the first response
	if !sh.init {
		sh.init = true
		sh.initCh <- strToError(resp.Error)
		return
	}

	// Decode logs for all other responses
	var rec map[string]interface{}
	if err := sh.client.dec.Decode(&rec); err != nil {
		log.Printf("[ERR] Failed to decode stream record: %v", err)
		sh.client.deregisterHandler(sh.seq)
		return
	}
	select {
	case sh.eventCh <- rec:
	default:
		log.Printf("[ERR] Dropping event! Stream channel full")
	}
}

func (sh *streamHandler) Cleanup() {
	if !sh.closed {
		if !sh.init {
			sh.init = true
			sh.initCh <- errors.New("Stream closed")
		}
		if sh.eventCh != nil {
			close(sh.eventCh)
		}
		sh.closed = true
	}
}
```

**File:** client/rpc_client.go (L585-649)
```go
type queryHandler struct {
	client *RPCClient
	closed bool
	init   bool
	initCh chan<- error
	ackCh  chan<- string
	respCh chan<- NodeResponse
	seq    uint64
}

func (qh *queryHandler) Handle(resp *responseHeader) {
	// Initialize on the first response
	if !qh.init {
		qh.init = true
		qh.initCh <- strToError(resp.Error)
		return
	}

	// Decode the query response
	var rec queryRecord
	if err := qh.client.dec.Decode(&rec); err != nil {
		log.Printf("[ERR] Failed to decode query response: %v", err)
		qh.client.deregisterHandler(qh.seq)
		return
	}

	switch rec.Type {
	case queryRecordAck:
		select {
		case qh.ackCh <- rec.From:
		default:
			log.Printf("[ERR] Dropping query ack, channel full")
		}

	case queryRecordResponse:
		select {
		case qh.respCh <- NodeResponse{rec.From, rec.Payload}:
		default:
			log.Printf("[ERR] Dropping query response, channel full")
		}

	case queryRecordDone:
		// No further records coming
		qh.client.deregisterHandler(qh.seq)

	default:
		log.Printf("[ERR] Unrecognized query record type: %s", rec.Type)
	}
}

func (qh *queryHandler) Cleanup() {
	if !qh.closed {
		if !qh.init {
			qh.init = true
			qh.initCh <- errors.New("Stream closed")
		}
		if qh.ackCh != nil {
			close(qh.ackCh)
		}
		if qh.respCh != nil {
			close(qh.respCh)
		}
		qh.closed = true
	}
}
```

**File:** client/rpc_client.go (L751-786)
```go
// genericRPC is used to send a request and wait for an
// errorSequenceResponse, potentially returning an error
func (c *RPCClient) genericRPC(header *requestHeader, req interface{}, resp interface{}) error {
	// Setup a response handler
	errCh := make(chan error, 1)
	handler := func(respHeader *responseHeader) {
		// If we get an auth error, we should not wait for a request body
		if respHeader.Error == authRequired {
			goto SEND_ERR
		}
		if resp != nil {
			err := c.dec.Decode(resp)
			if err != nil {
				errCh <- err
				return
			}
		}
	SEND_ERR:
		errCh <- strToError(respHeader.Error)
	}
	c.handleSeq(header.Seq, &seqCallback{handler: handler})
	defer c.deregisterHandler(header.Seq)

	// Send the request
	if err := c.send(header, req); err != nil {
		return err
	}

	// Wait for a response
	select {
	case err := <-errCh:
		return err
	case <-c.shutdownCh:
		return clientClosed
	}
}
```

**File:** client/rpc_client.go (L796-799)
```go
// getSeq returns the next sequence number in a safe manner
func (c *RPCClient) getSeq() uint64 {
	return atomic.AddUint64(&c.seq, 1)
}
```

**File:** client/rpc_client.go (L812-822)
```go
// deregisterHandler is used to deregister a handler
func (c *RPCClient) deregisterHandler(seq uint64) {
	c.dispatchLock.Lock()
	seqH, ok := c.dispatch[seq]
	delete(c.dispatch, seq)
	c.dispatchLock.Unlock()

	if ok {
		seqH.Cleanup()
	}
}
```

**File:** client/rpc_client.go (L824-830)
```go
// handleSeq is used to setup a handlerto wait on a response for
// a given sequence number.
func (c *RPCClient) handleSeq(seq uint64, handler seqHandler) {
	c.dispatchLock.Lock()
	defer c.dispatchLock.Unlock()
	c.dispatch[seq] = handler
}
```

**File:** client/rpc_client.go (L832-842)
```go
// respondSeq is used to respond to a given sequence number
func (c *RPCClient) respondSeq(seq uint64, respHeader *responseHeader) {
	c.dispatchLock.Lock()
	seqL, ok := c.dispatch[seq]
	c.dispatchLock.Unlock()

	// Get a registered listener, ignore if none
	if ok {
		seqL.Handle(respHeader)
	}
}
```

**File:** client/rpc_client.go (L844-858)
```go
// listen is used to processes data coming over the IPC channel,
// and wrote it to the correct destination based on seq no
func (c *RPCClient) listen() {
	defer c.Close()
	var respHeader responseHeader
	for {
		if err := c.dec.Decode(&respHeader); err != nil {
			if !c.shutdown {
				log.Printf("[ERR] agent.client: Failed to decode response header: %v", err)
			}
			break
		}
		c.respondSeq(respHeader.Seq, &respHeader)
	}
}
```

**File:** client/const.go (L18-39)
```go
const (
	handshakeCommand       = "handshake"
	eventCommand           = "event"
	forceLeaveCommand      = "force-leave"
	joinCommand            = "join"
	membersCommand         = "members"
	membersFilteredCommand = "members-filtered"
	streamCommand          = "stream"
	stopCommand            = "stop"
	monitorCommand         = "monitor"
	leaveCommand           = "leave"
	installKeyCommand      = "install-key"
	useKeyCommand          = "use-key"
	removeKeyCommand       = "remove-key"
	listKeysCommand        = "list-keys"
	tagsCommand            = "tags"
	queryCommand           = "query"
	respondCommand         = "respond"
	authCommand            = "auth"
	statsCommand           = "stats"
	getCoordinateCommand   = "get-coordinate"
)
```

**File:** client/const.go (L60-64)
```go
// Request header is sent before each request
type requestHeader struct {
	Command string
	Seq     uint64
}
```

**File:** client/const.go (L66-70)
```go
// Response header is sent before each response
type responseHeader struct {
	Seq   uint64
	Error string
}
```

**File:** cmd/serf/command/agent/ipc.go (L384-420)
```go
// listen is a long running routine that listens for new clients
func (i *AgentIPC) listen() {
	for {
		conn, err := i.listener.Accept()
		if err != nil {
			if i.isStopped() {
				return
			}
			i.logger.Printf("[ERR] agent.ipc: Failed to accept client: %v", err)
			continue
		}
		i.logger.Printf("[INFO] agent.ipc: Accepted client: %v", conn.RemoteAddr())
		metrics.IncrCounterWithLabels([]string{"agent", "ipc", "accept"}, 1, nil)

		// Wrap the connection in a client
		client := &IPCClient{
			name:           conn.RemoteAddr().String(),
			conn:           conn,
			reader:         bufio.NewReader(conn),
			writer:         bufio.NewWriter(conn),
			eventStreams:   make(map[uint64]*eventStream),
			pendingQueries: make(map[uint64]*serf.Query),
		}
		client.dec = codec.NewDecoder(client.reader, i.newMsgpackHandle())
		client.enc = codec.NewEncoder(client.writer, i.newMsgpackHandle())

		// Register the client
		i.Lock()
		if !i.isStopped() {
			i.clients[client.name] = client
			go i.handleClient(client)
		} else {
			conn.Close()
		}
		i.Unlock()
	}
}
```

**File:** cmd/serf/command/agent/ipc.go (L471-557)
```go
// handleRequest is used to evaluate a single client command
func (i *AgentIPC) handleRequest(client *IPCClient, reqHeader *requestHeader) error {
	// Look for a command field
	command := reqHeader.Command
	seq := reqHeader.Seq

	// Ensure the handshake is performed before other commands
	if command != handshakeCommand && client.version == 0 {
		respHeader := responseHeader{Seq: seq, Error: handshakeRequired}
		client.Send(&respHeader, nil)
		return fmt.Errorf(handshakeRequired)
	}
	metrics.IncrCounterWithLabels([]string{"agent", "ipc", "command"}, 1, nil)

	// Ensure the client has authenticated after the handshake if necessary
	if i.authKey != "" && !client.didAuth && command != authCommand && command != handshakeCommand {
		i.logger.Printf("[WARN] agent.ipc: Client sending commands before auth")
		respHeader := responseHeader{Seq: seq, Error: authRequired}
		client.Send(&respHeader, nil)
		return nil
	}

	// Dispatch command specific handlers
	switch command {
	case handshakeCommand:
		return i.handleHandshake(client, seq)

	case authCommand:
		return i.handleAuth(client, seq)

	case eventCommand:
		return i.handleEvent(client, seq)

	case membersCommand, membersFilteredCommand:
		return i.handleMembers(client, command, seq)

	case streamCommand:
		return i.handleStream(client, seq)

	case monitorCommand:
		return i.handleMonitor(client, seq)

	case stopCommand:
		return i.handleStop(client, seq)

	case forceLeaveCommand:
		return i.handleForceLeave(client, seq)

	case joinCommand:
		return i.handleJoin(client, seq)

	case leaveCommand:
		return i.handleLeave(client, seq)

	case installKeyCommand:
		return i.handleInstallKey(client, seq)

	case useKeyCommand:
		return i.handleUseKey(client, seq)

	case removeKeyCommand:
		return i.handleRemoveKey(client, seq)

	case listKeysCommand:
		return i.handleListKeys(client, seq)

	case tagsCommand:
		return i.handleTags(client, seq)

	case queryCommand:
		return i.handleQuery(client, seq)

	case respondCommand:
		return i.handleRespond(client, seq)

	case statsCommand:
		return i.handleStats(client, seq)

	case getCoordinateCommand:
		return i.handleGetCoordinate(client, seq)

	default:
		respHeader := responseHeader{Seq: seq, Error: unsupportedCommand}
		client.Send(&respHeader, nil)
		return fmt.Errorf("command '%s' not recognized", command)
	}
}
```
