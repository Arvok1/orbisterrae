import { Envelope } from '@orbis/protocol';
import { useAtlas } from './state.js';

/** Snapshot transport for the P0.5 diagnostic world, via the Vite WS proxy. */
export function connectWorld() {
  let disposed = false;
  let seq = 0n;
  let socket: WebSocket | null = null;
  let retry: ReturnType<typeof setTimeout> | undefined;
  let throttle: ReturnType<typeof setTimeout> | undefined;
  let lastViewport = '';
  function send() {
    const viewport = useAtlas.getState().viewport;
    if (!viewport || socket?.readyState !== WebSocket.OPEN) return;
    const key = JSON.stringify(viewport);
    if (key === lastViewport) return;
    lastViewport = key;
    seq++;
    socket.send(Envelope.encode(Envelope.fromPartial({
      seq, body: {$case: 'command', command: {requestId: `viewport-${seq}`,
        kind: {$case: 'viewport', viewport}}}
    })).finish());
  }
  function connect() {
    if (disposed) return;
    useAtlas.setState({connection: 'connecting'});
    socket = new WebSocket(`${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws`);
    socket.binaryType = 'arraybuffer';
    socket.onopen = () => { lastViewport = ''; useAtlas.setState({connection: 'online'}); send(); };
    socket.onmessage = (event: MessageEvent<ArrayBuffer>) => {
      try {
        const message = Envelope.decode(new Uint8Array(event.data));
        if (message.seq !== seq || message.body?.$case !== 'server') return;
        const kind = message.body.server.kind;
        if (kind?.$case === 'snapshot') useAtlas.setState({entities: kind.snapshot.entities, connection: 'online'});
        if (kind?.$case === 'result' && !kind.result.ok) useAtlas.setState({connection: 'offline', entities: []});
      } catch { socket?.close(); }
    };
    socket.onclose = () => {
      if (disposed) return;
      useAtlas.setState({connection: 'offline', entities: []});
      retry = setTimeout(connect, 2000);
    };
  }
  const unsubscribe = useAtlas.subscribe((state, before) => {
    if (state.viewport === before.viewport) return;
    clearTimeout(throttle);
    throttle = setTimeout(send, 150);
  });
  connect();
  return () => { disposed = true; clearTimeout(retry); clearTimeout(throttle); unsubscribe(); socket?.close(); };
}
