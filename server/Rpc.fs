namespace Remoting

open MBrace.FsPickler
open SharedMemory

module Rpc =
    type RpcContext<'command, 'message> = 
        { name: string; buffer: RpcBuffer; post: 'command -> Async<'message>} with
        interface System.IDisposable with 
            member this.Dispose() = this.buffer.Dispose()

    let createHost name (handler : 'command -> Async<'message>) =
        let pickler = FsPickler.CreateBinarySerializer()        
        let onReceive data = Async.StartImmediateAsTask <| async {
                let command = pickler.UnPickle data
                let! message = handler command
                return pickler.Pickle message
        }
        { name = name; buffer = new RpcBuffer(name, (fun _ data -> onReceive data)); post = handler }
    
    let createClient name =
        let pickler = FsPickler.CreateBinarySerializer()        
        let buffer = new RpcBuffer(name)        
        let onSend command = async {
                let data = pickler.Pickle command
                let! response = buffer.RemoteRequestAsync(data, 1000) |> Async.AwaitTask
                if not response.Success then
                    return None                
                else 
                    return Some <| pickler.UnPickle response.Data
        }
            
        { name = name; buffer = new RpcBuffer(name); post = onSend }
