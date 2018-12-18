﻿namespace Fake.Deploy

open System
open System.Collections.Concurrent
open Nancy.Security

[<System.Obsolete("This API is obsolete. There is no alternative in FAKE 5 yet. You can help by porting this module.")>]
type UserMap = 
    { User : IUserIdentity
      ValidTo : DateTime }

[<System.Obsolete("This API is obsolete. There is no alternative in FAKE 5 yet. You can help by porting this module.")>]
type UserMapper(createGuid : unit -> Guid) = 
    let users = ConcurrentDictionary<Guid, UserMap>()
    let expires() = DateTime.Now.AddMinutes(10.)
    
    let removeStale() = 
        let stale = users |> Seq.filter (fun r -> r.Value.ValidTo <= DateTime.Now)
        stale |> Seq.iter (fun s -> users.TryRemove(s.Key) |> ignore)
    
    [<System.Obsolete("This API is obsolete. There is no alternative in FAKE 5 yet. You can help by porting this module.")>]
    member this.GetUser token = 
        removeStale()
        match users.TryGetValue token with
        | false, _ -> None
        | _, u ->
            users.TryUpdate(token, { u with ValidTo = expires() }, u) |> ignore
            Some u.User
    [<System.Obsolete("This API is obsolete. There is no alternative in FAKE 5 yet. You can help by porting this module.")>]
    member this.RemoveByToken token = users.TryRemove(token) |> ignore
    
    [<System.Obsolete("This API is obsolete. There is no alternative in FAKE 5 yet. You can help by porting this module.")>]
    member this.RemoveByUser(user : IUserIdentity) = 
        if user <> null then 
            match users |> Seq.tryFind (fun x -> x.Value.User.UserName = user.UserName) with
            | None -> ()
            | Some u -> this.RemoveByToken u.Key
    
    [<System.Obsolete("This API is obsolete. There is no alternative in FAKE 5 yet. You can help by porting this module.")>]
    member this.AddUser user = 
        let id = createGuid()
        users.TryAdd(id, 
                     { User = user
                       ValidTo = expires() })
        |> ignore
        id
