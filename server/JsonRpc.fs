namespace Remoting

open System.IO.Pipes
open StreamJsonRpc
open System
open Microsoft.VisualStudio.Threading

module JsonRpc =

    type Server<'command, 'message> (handle: 'command -> Async<'message>) = 
        member _.Handle (command) = 
            handle command |> Async.StartImmediateAsTask 

    let createServer name handle = async { 
        let! token = Async.CancellationToken
        use stream = 
                   new NamedPipeServerStream(
                       name, 
                       PipeDirection.InOut, 
                       NamedPipeServerStream.MaxAllowedServerInstances, 
                       PipeTransmissionMode.Byte, 
                       PipeOptions.Asynchronous
                   )
        do! stream.WaitForConnectionAsync() |> Async.AwaitTask         
        let server = Server handle
        use jsonRpc = JsonRpc.Attach(stream, target = server)     
        token.Register (Action jsonRpc.Dispose) |> ignore
        do! jsonRpc.Completion |> Async.AwaitTask
    } 
    
    type RpcClient<'command, 'message> = 
        { 
            send: 'command -> Async<unit> 
            sendReceive: 'command -> Async<'message> 
            waitAsync: unit -> Async<unit>
            close: unit -> unit
        } with        
        interface System.IDisposable with 
            member this.Dispose() = this.close ()


    let createClient name = 
        let stream = 
            new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous)        
        let methodName = "Handle"
        stream.ConnectAsync() |> ignore
        let jsonRpc = new JsonRpc(stream)
        jsonRpc.SynchronizationContext <- new NonConcurrentSynchronizationContext(false)
        jsonRpc.StartListening()
        {
            send = fun cmd -> jsonRpc.NotifyAsync(methodName, argument = cmd) |> Async.AwaitTask
            sendReceive = fun cmd -> jsonRpc.InvokeAsync<_>(methodName, argument = cmd) |> Async.AwaitTask
            waitAsync = fun () -> jsonRpc.Completion |> Async.AwaitTask
            close = fun () -> jsonRpc.Dispose()
        }
