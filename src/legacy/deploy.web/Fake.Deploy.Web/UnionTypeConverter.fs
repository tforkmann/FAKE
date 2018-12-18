﻿namespace Fake.Deploy.Web.Helpers

open System
open System.IO
open System.Web
open System.Runtime.Serialization
open Microsoft.FSharp.Reflection
open Newtonsoft.Json

type internal NewtonsoftUnionTypeConverter() =
    inherit Newtonsoft.Json.JsonConverter()

    let doRead pos (reader: JsonReader) = 
        reader.Read() |> ignore 

    override x.CanConvert(typ:Type) =
        let result = 
            ((typ.GetInterface(typeof<System.Collections.IEnumerable>.FullName) = null) 
            && FSharpType.IsUnion typ)
        result

    override x.WriteJson(writer: JsonWriter, value: obj, serializer: JsonSerializer) =
        let t = value.GetType()
        let write (name : string) (fields : obj []) = 
            writer.WriteStartObject()
            writer.WritePropertyName("case")
            writer.WriteValue(name)  
            writer.WritePropertyName("values")
            serializer.Serialize(writer, fields)
            writer.WriteEndObject()   

        let (info, fields) = FSharpValue.GetUnionFields(value, t)
        write info.Name fields

    override x.ReadJson(reader: JsonReader, objectType: Type, existingValue: obj, serializer: JsonSerializer) =      
         let cases = FSharpType.GetUnionCases(objectType)
         if reader.TokenType <> JsonToken.Null  
         then 
            doRead "1" reader
            doRead "2" reader
            let case = cases |> Array.find(fun x -> x.Name = if reader.Value = null then "None" else reader.Value.ToString())
            doRead "3" reader
            doRead "4" reader
            doRead "5" reader
            let fields =  [| 
                   for field in case.GetFields() do
                       let result = serializer.Deserialize(reader, field.PropertyType)
                       reader.Read() |> ignore
                       yield result
             |] 
            let result = FSharpValue.MakeUnion(case, fields)
            while reader.TokenType <> JsonToken.EndObject do
                doRead "6" reader         
            result
         else
            FSharpValue.MakeUnion(cases.[0], [||]) 




