namespace eveproxy

open System
open System.Diagnostics.CodeAnalysis
open MongoDB.Bson
open MongoDB.Driver


[<ExcludeFromCodeCoverage>]
module MongoBson =
    open MongoDB.Bson.IO
    open MongoDB.Bson.Serialization

    let id () = ObjectId.GenerateNewId()

    let ofJson (json: string) = BsonSerializer.Deserialize<BsonDocument>(json)
        
    // TODO: expensive...
    let ofObject (value) =
        value |> Newtonsoft.Json.JsonConvert.SerializeObject |> ofJson

    let toObject<'a> (doc: BsonDocument) =        
        MongoDB.Bson.Serialization.BsonSerializer.Deserialize<'a>(doc)

    let setDocId (id) (doc: BsonDocument) =
        let existingId = doc.Elements |> Seq.filter (fun e -> e.Name = "_id") |> Seq.tryHead
        match existingId with
        | None -> 
            doc["_id"] <- id
            doc
        | _ -> doc

    let getDocId (bson: BsonDocument) =
        bson.Elements |> Seq.filter (fun e -> e.Name = "_id") |> Seq.head

    let getObjectId (bson: BsonDocument) =
        bson |> getDocId |> (fun id -> id.Value.AsObjectId)

    let getId (bson: BsonDocument) =
        bson |> getDocId |> (fun id -> id.Value.AsString)

[<ExcludeFromCodeCoverage>]
module Mongo =
    let private defaultMongoPort = 27017
    let private isMongoAtlas (server: string) = server.EndsWith(".mongodb.net")
    let private idFilter id = sprintf @"{ _id: ""%s"" }" id

    let private appendPort server =
        match server |> Strings.split ":" with
        | [| name; port |] -> server
        | [| x |] when isMongoAtlas x -> x
        | _ -> sprintf "%s:%i" server defaultMongoPort

    let connectionString (userName, password) server =
        let server = appendPort server
        let modifier = if isMongoAtlas server then "+srv" else ""

        match userName with
        | Strings.NullOrWhitespace _ -> sprintf "mongodb%s://%s" modifier server
        | name ->            
            sprintf "mongodb%s://%s:%s@%s" modifier name password server

    let setDbConnection dbName connectionString =
        match dbName with
        | Strings.NullOrWhitespace _ -> connectionString
        | x -> sprintf "%s/%s" connectionString dbName


    let initDb dbName (connection: string) =
        let client = MongoClient(connection)
        let db = client.GetDatabase(dbName)

        try
            new MongoDB.Driver.BsonDocumentCommand<Object>(BsonDocument.Parse("{ping:1}"))
            |> db.RunCommand
            |> ignore

            db
        with :? System.TimeoutException as ex ->
            raise (
                new ApplicationException "Cannot connect to DB. Check the server name, credentials & firewalls are correct."
            )

    let setIndex (path: string) (collection: IMongoCollection<'a>) =
        let json = sprintf "{'%s': 1 }" path
        let def = IndexKeysDefinition<'a>.op_Implicit (json)
        let model = CreateIndexModel<'a>(def)
        let r = collection.Indexes.CreateOne(model)

        collection

    let getCollection colName (db: IMongoDatabase) = db.GetCollection(colName)

    let initCollection indexPath server dbName collectionName (userName, password) =
        let col =
            server
            |> connectionString (userName, password)
            |> setDbConnection dbName
            |> initDb dbName
            |> getCollection collectionName

        if indexPath <> "" then col |> setIndex indexPath else col

    let upsert (collection: IMongoCollection<BsonDocument>) (doc: BsonDocument) =
        let opts = ReplaceOptions()
        opts.IsUpsert <- true

        let filter =
            doc
            |> MongoBson.getId
            |> idFilter
            |> MongoBson.ofJson
            |> FilterDefinition.op_Implicit

        collection.ReplaceOneAsync(filter, doc, opts)

    let delete (collection: IMongoCollection<BsonDocument>) id =
        let filter = id |> idFilter |> MongoBson.ofJson |> FilterDefinition.op_Implicit
        collection.DeleteOneAsync(filter)

    let query<'a> (collection: IMongoCollection<BsonDocument>) =
        collection.AsQueryable<BsonDocument>() |> Seq.map MongoBson.toObject<'a>

    let getSingle<'a> (collection: IMongoCollection<BsonDocument>) (predicate: string) =
        task {
            let fieldFilter = new JsonFilterDefinition<BsonDocument>(predicate)
            use! r = collection.FindAsync(fieldFilter)

            return
                r.FirstOrDefault<BsonDocument>()
                |> Option.ofNull
                |> Option.map MongoBson.toObject<'a>
        }


    let count (collection: IMongoCollection<BsonDocument>) =
        new BsonDocument()
        |> FilterDefinition.op_Implicit
        |> collection.CountDocumentsAsync


    let pullSingletonFromQueue<'a> (collection: IMongoCollection<BsonDocument>) =
        task {
            let filter = new MongoDB.Bson.BsonDocument() |> FilterDefinition.op_Implicit
            let opts = new FindOneAndDeleteOptions<MongoDB.Bson.BsonDocument>()

            let! r = collection.FindOneAndDeleteAsync<MongoDB.Bson.BsonDocument>(filter, opts)

            return
                match Object.ReferenceEquals(r, null) with
                | true -> None
                | _ -> r |> MongoBson.toObject<'a> |> Some
        }

    let pushToQueue<'a> (collection: IMongoCollection<BsonDocument>) (values: seq<'a>) =
        task {
            let values =
                values
                |> Seq.map MongoBson.ofObject
                |> Array.ofSeq

            if values.Length > 0 then
                let opts = new InsertManyOptions()
                
                try
                    do! collection.InsertManyAsync(values, opts)
                with ex ->
                    ignore ex // TODO: future ... telemetry.ex ex
        }
