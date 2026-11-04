import { Envelope, type Command, type ServerMsg } from '@orbis/protocol';

export class BotClient {
  private seq = 0n;
  private pending = false;
  private constructor(private socket: WebSocket, private timeoutMs: number) {}

  static async open(url: string, timeoutMs: number): Promise<BotClient> {
    const socket = new WebSocket(url);
    socket.binaryType = 'arraybuffer';
    try {
      await new Promise<void>((resolve,reject) => {
        const timer = setTimeout(()=>{socket.close();reject(new Error('Bot connection timeout'));},timeoutMs);
        socket.addEventListener('open',()=>{clearTimeout(timer);resolve();},{once:true});
        socket.addEventListener('error',()=>{clearTimeout(timer);reject(new Error('Bot connection failed'));},{once:true});
      });
      return new BotClient(socket, timeoutMs);
    } catch (error) { socket.close(); throw error; }
  }

  async request(kind: Command['kind']): Promise<ServerMsg> {
    if (this.pending) throw new Error('Only one in-flight request per bot');
    this.pending = true;
    const seq = ++this.seq;
    let timer: ReturnType<typeof setTimeout>;
    let onMessage: (event: MessageEvent<ArrayBuffer>)=>void;
    let onClose: ()=>void;
    try {
      return await new Promise<ServerMsg>((resolve,reject) => {
        timer = setTimeout(()=>reject(new Error('Bot reply timeout')),this.timeoutMs);
        onMessage = event => {
          try {
            const response = Envelope.decode(new Uint8Array(event.data));
            if (response.seq!==seq || response.body?.$case!=='server') throw new Error('Mismatched bot reply');
            resolve(response.body.server);
          } catch (error) { reject(error); }
        };
        onClose = ()=>reject(new Error('Bot socket closed before reply'));
        this.socket.addEventListener('message',onMessage,{once:true});
        this.socket.addEventListener('close',onClose,{once:true});
        this.socket.send(Envelope.encode(Envelope.fromPartial({seq,body:{$case:'command',command:{requestId:`bot-${seq}`,kind}}})).finish());
      });
    } finally {
      clearTimeout(timer!);
      this.socket.removeEventListener('message',onMessage!);
      this.socket.removeEventListener('close',onClose!);
      this.pending = false;
    }
  }

  close() { this.socket.close(); }
}
