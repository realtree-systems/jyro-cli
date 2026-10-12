module RealTree.Jyro.Cli.JsonSerializer

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Encodings.Web
open System.Text.Json

/// Serializes a JyroValue object graph to indented JSON without using JsonSerializer.Serialize(object),
/// which requires reflection and is not trim-safe.
let serializeJyroValue (value: obj) =
    use stream = new MemoryStream()

    use writer =
        new Utf8JsonWriter(
            stream,
            JsonWriterOptions(Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)
        )

    let rec writeValue (v: obj) =
        match v with
        | null -> writer.WriteNullValue()
        | :? bool as b -> writer.WriteBooleanValue(b)
        | :? int64 as l -> writer.WriteNumberValue(l)
        | :? double as d -> writeDouble d
        | :? string as s -> writer.WriteStringValue(s)
        | :? List<obj> as list ->
            writer.WriteStartArray()

            for item in list do
                writeValue item

            writer.WriteEndArray()
        | :? Dictionary<string, obj> as dict ->
            writer.WriteStartObject()

            for kvp in dict do
                writer.WritePropertyName(kvp.Key)
                writeValue kvp.Value

            writer.WriteEndObject()
        | _ -> writer.WriteStringValue(v.ToString())

    and writeDouble (value: double) =
        if Double.IsFinite(value) && value = Math.Floor(value) && Math.Abs(value) < 1e15 then
            // Write whole-number doubles with .0 to preserve float type in JSON
            writer.WriteRawValue($"{value:F1}")
        else
            // Round to 10 decimal places to eliminate IEEE 754 precision artifacts
            writer.WriteNumberValue(Math.Round(value, 10))

    writeValue value
    writer.Flush()
    Encoding.UTF8.GetString(stream.ToArray())

/// Serializes a JyroValue object graph directly to a stream without intermediate string allocation.
let serializeJyroValueToStream (value: obj) (outputStream: Stream) =
    use writer =
        new Utf8JsonWriter(
            outputStream,
            JsonWriterOptions(Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)
        )

    let flushThreshold = 64 * 1024

    let rec writeValue (v: obj) =
        match v with
        | null -> writer.WriteNullValue()
        | :? bool as b -> writer.WriteBooleanValue(b)
        | :? int64 as l -> writer.WriteNumberValue(l)
        | :? double as d -> writeDouble d
        | :? string as s -> writer.WriteStringValue(s)
        | :? List<obj> as list ->
            writer.WriteStartArray()

            for item in list do
                writeValue item
                if writer.BytesPending > flushThreshold then writer.Flush()

            writer.WriteEndArray()
        | :? Dictionary<string, obj> as dict ->
            writer.WriteStartObject()

            for kvp in dict do
                writer.WritePropertyName(kvp.Key)
                writeValue kvp.Value
                if writer.BytesPending > flushThreshold then writer.Flush()

            writer.WriteEndObject()
        | _ -> writer.WriteStringValue(v.ToString())

    and writeDouble (value: double) =
        if Double.IsFinite(value) && value = Math.Floor(value) && Math.Abs(value) < 1e15 then
            writer.WriteRawValue($"{value:F1}")
        else
            writer.WriteNumberValue(Math.Round(value, 10))

    writeValue value
    writer.Flush()
