import { Envelope, type Command } from '@orbis/protocol';
import { useAtlas } from './state.js';

let activeSend:((kind:Command['kind'])=>void)|null=null;
export function sendGame(kind:Command['kind']) {
  if (activeSend) activeSend(kind);else useAtlas.setState({gameMessage:'authentication_required'});
}

/** Same binary command path for viewports, ruler movement and village foundation. */
export function connectWorld(token:string|null) {
  let disposed=false,seq=0n,socket:WebSocket|null=null,retry:ReturnType<typeof setTimeout>|undefined;
  let authenticated=false,lastViewport='';
  const pending=new Map<bigint,{type:'auth'|'view'|'write';at:number}>();
  function send(kind:Command['kind'],type:'auth'|'view'|'write') {
    if (socket?.readyState!==WebSocket.OPEN) return;
    const id=++seq;
    pending.set(id,{type,at:performance.now()});
    socket.send(Envelope.encode(Envelope.fromPartial({seq:id,body:{$case:'command',command:{requestId:crypto.randomUUID(),kind}}})).finish());
  }
  function poll() {
    if (token&&!authenticated || [...pending.values()].some(item=>item.type==='view'||item.type==='auth')) return;
    const viewport=useAtlas.getState().viewport;
    if (!viewport) return;
    const key=JSON.stringify(viewport);
    if (!token&&lastViewport===key) return;
    lastViewport=key;
    send({$case:'viewport',viewport},'view');
  }
  const sendAction=(kind:Command['kind'])=>{
    if (!authenticated || !token || socket?.readyState!==WebSocket.OPEN) {useAtlas.setState({gameMessage:'authentication_required'});return;}
    if ([...pending.values()].some(item=>item.type==='write')) return;
    useAtlas.setState({commandPending:true,gameMessage:null});send(kind,'write');
  };
  activeSend=sendAction;
  function connect() {
    if (disposed) return;
    authenticated=false;lastViewport='';pending.clear();
    useAtlas.setState({connection:'connecting'});
    socket=new WebSocket(`${location.protocol==='https:'?'wss':'ws'}://${location.host}/ws`);
    socket.binaryType='arraybuffer';
    socket.onopen=()=>{
      useAtlas.setState({connection:'online'});
      if (token) send({$case:'authenticate',authenticate:{token}},'auth');else poll();
    };
    socket.onmessage=(event:MessageEvent<ArrayBuffer>)=>{
      try {
        const response=Envelope.decode(new Uint8Array(event.data));
        const request=pending.get(response.seq);pending.delete(response.seq);
        if (!request || response.body?.$case!=='server') return;
        const kind=response.body.server.kind;
        if (request.type==='auth') {
          if (kind?.$case==='result'&&kind.result.ok) {authenticated=true;poll();}
          else {useAtlas.getState().setSession(null);useAtlas.setState({gameMessage:'authentication_required'});}
          return;
        }
        if (kind?.$case==='snapshot') useAtlas.setState({entities:kind.snapshot.entities,connection:'online'});
        if (kind?.$case==='world') {
          const first=useAtlas.getState().world===null;
          useAtlas.setState({world:kind.world,serverOffsetMs:kind.world.nowMs-Date.now(),connection:'online'});
          if (first) useAtlas.getState().command('follow');
        }
        if (kind?.$case==='result') {
          useAtlas.setState({commandPending:false,gameMessage:kind.result.ok?'command_accepted':kind.result.error});
          if (request.type==='write') poll();
        }
      } catch {socket?.close();}
    };
    socket.onclose=()=>{
      if (disposed) return;
      pending.clear();useAtlas.setState({connection:'offline',commandPending:false});
      retry=setTimeout(connect,2000);
    };
  }
  const timer=setInterval(()=>{
    if ([...pending.values()].some(item=>performance.now()-item.at>10000)) {socket?.close();return;}
    poll();
  },token?500:150);
  connect();
  return ()=>{
    disposed=true;clearInterval(timer);clearTimeout(retry);pending.clear();socket?.close();
    if (activeSend===sendAction) activeSend=null;
  };
}
