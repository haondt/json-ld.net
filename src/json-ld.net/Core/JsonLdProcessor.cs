using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using JsonLD.Core;
using JsonLD.Impl;
using Newtonsoft.Json.Linq;

namespace JsonLD.Core
{
    /// <summary>http://json-ld.org/spec/latest/json-ld-api/#the-jsonldprocessor-interface
    /// 	</summary>
    /// <author>tristan</author>
    public class JsonLdProcessor
    {
        /// <exception cref="JsonLD.Core.JsonLdError"></exception>
        public static async Task<JObject> CompactAsync(JToken input, JToken context, JsonLdOptions opts)
        {
            // 1)
            // TODO: look into java futures/promises
            // 2-6) NOTE: these are all the same steps as in expand
            JToken expanded = await ExpandAsync(input, opts).ConfigureAwait(false); 
            // 7)
            if (context is JObject && ((IDictionary<string, JToken>)context).ContainsKey(
                "@context"))
            {
                context = ((JObject)context)["@context"];
            }
            Context activeCtx = new Context(opts);
            activeCtx = await activeCtx.ParseAsync(context).ConfigureAwait(false);
            // 8)
            JToken compacted = new JsonLdApi(opts).Compact(activeCtx, null, expanded, opts.GetCompactArrays
                ());
            // final step of Compaction Algorithm
            // TODO: SPEC: the result result is a NON EMPTY array,
            if (compacted is JArray)
            {
                if (((JArray)compacted).IsEmpty())
                {
                    compacted = new JObject();
                }
                else
                {
                    JObject tmp = new JObject();
                    // TODO: SPEC: doesn't specify to use vocab = true here
                    tmp[activeCtx.CompactIri("@graph", true)] = compacted;
                    compacted = tmp;
                }
            }
            if (!compacted.IsNull() && !context.IsNull())
            {
                // TODO: figure out if we can make "@context" appear at the start of
                // the keySet
                if ((context is JObject && !((JObject)context).IsEmpty())
                     || (context is JArray && !((JArray)context).IsEmpty()))
                {
                    compacted["@context"] = context;
                }
            }
            // 9)
            return (JObject)compacted;
        }

        /// <exception cref="JsonLD.Core.JsonLdError"></exception>
        public static async Task<JArray> ExpandAsync(JToken input, JsonLdOptions opts)
        {

            // 2) verification of DOMString IRI
            bool isIriString = input.Type == JTokenType.String;
            if (isIriString)
            {
                bool hasColon = false;
                foreach (var c in ((string) input))
                {
                    if (c == ':')
                    {
                        hasColon = true;
                    }
                    
                    if (!hasColon && (c == '{' || c == '['))
                    {
                        isIriString = false;
                        break;
                    }
                }
            }

            if (isIriString)
            {
                try
                {
                    RemoteDocument tmp = await opts.documentLoader.LoadDocumentAsync((string)input).ConfigureAwait(false);
                    input = tmp.document;
                }
                catch (Exception e)
                {
                    // TODO: figure out how to deal with remote context
                    throw new JsonLdError(JsonLdError.Error.LoadingDocumentFailed, e.Message);
                }
                // if set the base in options should override the base iri in the
                // active context
                // thus only set this as the base iri if it's not already set in
                // options
                if (opts.GetBase() == null)
                {
                    opts.SetBase((string)input);
                }
            }
            // 3)
            Context activeCtx = new Context(opts);
            // 4)
            if (opts.GetExpandContext() != null)
            {
                JObject exCtx = opts.GetExpandContext();
                if (exCtx is JObject && ((IDictionary<string, JToken>)exCtx).ContainsKey("@context"
                    ))
                {
                    exCtx = (JObject)((IDictionary<string, JToken>)exCtx)["@context"];
                }
                activeCtx = await activeCtx.ParseAsync(exCtx).ConfigureAwait(false);
            }
            // 5)
            // TODO: add support for getting a context from HTTP when content-type
            // is set to a jsonld compatable format
            // 6)
            JToken expanded = await new JsonLdApi(opts).ExpandAsync(activeCtx, input).ConfigureAwait(false);
            // final step of Expansion Algorithm
            if (expanded is JObject && ((IDictionary<string,JToken>)expanded).ContainsKey("@graph") && (
                (IDictionary<string, JToken>)expanded).Count == 1)
            {
                expanded = ((JObject)expanded)["@graph"];
            }
            else
            {
                if (expanded.IsNull())
                {
                    expanded = new JArray();
                }
            }
            // normalize to an array
            if (!(expanded is JArray))
            {
                JArray tmp = new JArray();
                tmp.Add(expanded);
                expanded = tmp;
            }
            return (JArray)expanded;
        }

        /// <exception cref="JsonLD.Core.JsonLdError"></exception>
        public static Task<JArray> ExpandAsync(JToken input)
        {
            return ExpandAsync(input, new JsonLdOptions(string.Empty));
        }

        /// <exception cref="JsonLD.Core.JsonLdError"></exception>
        public static async Task<JToken> FlattenAsync(JToken input, JToken context, JsonLdOptions opts)
        {
            // 2-6) NOTE: these are all the same steps as in expand
            JArray expanded = await ExpandAsync(input, opts).ConfigureAwait(false);
            // 7)
            if (context is JObject && ((IDictionary<string, JToken>)context).ContainsKey(
                "@context"))
            {
                context = context["@context"];
            }
            // 8) NOTE: blank node generation variables are members of JsonLdApi
            // 9) NOTE: the next block is the Flattening Algorithm described in
            // http://json-ld.org/spec/latest/json-ld-api/#flattening-algorithm
            // 1)
            JObject nodeMap = new JObject();
            nodeMap["@default"] = new JObject();
            // 2)
            new JsonLdApi(opts).GenerateNodeMap(expanded, nodeMap);
            // 3)
            JObject defaultGraph = (JObject)JsonLD.Collections.Remove
                (nodeMap, "@default");
            // 4)
            foreach (string graphName in nodeMap.GetKeys())
            {
                JObject graph = (JObject)nodeMap[graphName];
                // 4.1+4.2)
                JObject entry;
                if (!defaultGraph.ContainsKey(graphName))
                {
                    entry = new JObject();
                    entry["@id"] = graphName;
                    defaultGraph[graphName] = entry;
                }
                else
                {
                    entry = (JObject)defaultGraph[graphName];
                }
                // 4.3)
                // TODO: SPEC doesn't specify that this should only be added if it
                // doesn't exists
                if (!entry.ContainsKey("@graph"))
                {
                    entry["@graph"] = new JArray();
                }
                JArray keys = new JArray(graph.GetKeys());
                keys.SortInPlace();
                foreach (string id in keys)
                {
                    JObject node = (JObject)graph[id];
                    if (!(node.ContainsKey("@id") && node.Count == 1))
                    {
                        ((JArray)entry["@graph"]).Add(node);
                    }
                }
            }
            // 5)
            JArray flattened = new JArray();
            // 6)
            JArray keys_1 = new JArray(defaultGraph.GetKeys());
            keys_1.SortInPlace();
            foreach (string id_1 in keys_1)
            {
                JObject node = (JObject)defaultGraph[id_1
                    ];
                if (!(node.ContainsKey("@id") && node.Count == 1))
                {
                    flattened.Add(node);
                }
            }
            // 8)
            if (!context.IsNull() && !flattened.IsEmpty())
            {
                Context activeCtx = new Context(opts);
                activeCtx = await activeCtx.ParseAsync(context).ConfigureAwait(false);
                // TODO: only instantiate one jsonldapi
                JToken compacted = new JsonLdApi(opts).Compact(activeCtx, null, flattened, opts.GetCompactArrays
                    ());
                if (!(compacted is JArray))
                {
                    JArray tmp = new JArray();
                    tmp.Add(compacted);
                    compacted = tmp;
                }
                string alias = activeCtx.CompactIri("@graph");
                JObject rval = activeCtx.Serialize();
                rval[alias] = compacted;
                return rval;
            }
            return flattened;
        }

        /// <exception cref="JsonLD.Core.JsonLdError"></exception>
        public static Task<JToken> FlattenAsync(JToken input, JsonLdOptions opts)
        {
            return FlattenAsync(input, null, opts);
        }

        /// <exception cref="JsonLD.Core.JsonLdError"></exception>
        public static async Task<JObject> FrameAsync(JToken input, JToken frame, JsonLdOptions
             options)
        {
            if (frame is JObject)
            {
                frame = JsonLdUtils.Clone((JObject)frame);
            }
            // TODO string/IO input
            JToken expandedInput = await ExpandAsync(input, options).ConfigureAwait(false);
            JArray expandedFrame = await ExpandAsync(frame, options).ConfigureAwait(false);
            JsonLdApi api = await JsonLdApi.CreateAsync(expandedInput, options).ConfigureAwait(false);
            JArray framed = api.Frame(expandedInput, expandedFrame);
            Context activeCtx = await api.context.ParseAsync(frame["@context"
                ]).ConfigureAwait(false);
            JToken compacted = api.Compact(activeCtx, null, framed);
            if (!(compacted is JArray))
            {
                JArray tmp = new JArray();
                tmp.Add(compacted);
                compacted = tmp;
            }
            string alias = activeCtx.CompactIri("@graph");
            JObject rval = activeCtx.Serialize();
            rval[alias] = compacted;
            JsonLdUtils.RemovePreserve(activeCtx, rval, options);
            return rval;
        }

        private sealed class _Dictionary_242 : Dictionary<string, IRDFParser>
        {
            public _Dictionary_242()
            {
                {
                    // automatically register nquad serializer
                    this["application/nquads"] = new NQuadRDFParser();
                    this["text/turtle"] = new TurtleRDFParser();
                }
            }
        }

        /// <summary>
        /// a registry for RDF Parsers (in this case, JSONLDSerializers) used by
        /// fromRDF if no specific serializer is specified and options.format is set.
        /// </summary>
        /// <remarks>
        /// a registry for RDF Parsers (in this case, JSONLDSerializers) used by
        /// fromRDF if no specific serializer is specified and options.format is set.
        /// TODO: this would fit better in the document loader class
        /// </remarks>
        private static IDictionary<string, IRDFParser> rdfParsers = new _Dictionary_242();

        public static void RegisterRDFParser(string format, IRDFParser parser)
        {
            rdfParsers[format] = parser;
        }

        public static void RemoveRDFParser(string format)
        {
            JsonLD.Collections.Remove(rdfParsers, format);
        }

        /// <summary>Converts an RDF dataset to JSON-LD.</summary>
        /// <remarks>Converts an RDF dataset to JSON-LD.</remarks>
        /// <param name="dataset">
        /// a serialized string of RDF in a format specified by the format
        /// option or an RDF dataset to convert.
        /// </param>
        /// <?></?>
        /// <param name="callback">(err, output) called once the operation completes.</param>
        /// <exception cref="JsonLDNet.Core.JsonLdError"></exception>
        public static Task<JToken> FromRDFAsync(JToken dataset, JsonLdOptions options)
        {
            // handle non specified serializer case
            IRDFParser parser = null;
            if (options.format == null && dataset.Type == JTokenType.String)
            {
                // attempt to parse the input as nquads
                options.format = "application/nquads";
            }
            if (rdfParsers.ContainsKey(options.format))
            {
                parser = rdfParsers[options.format];
            }
            else
            {
                throw new JsonLdError(JsonLdError.Error.UnknownFormat, options.format);
            }
            // convert from RDF
            return FromRDFAsync(dataset, options, parser);
        }

        /// <exception cref="JsonLD.Core.JsonLdError"></exception>
        public static Task<JToken> FromRDFAsync(JToken dataset)
        {
            return FromRDFAsync(dataset, new JsonLdOptions(string.Empty));
        }

        /// <summary>Uses a specific serializer.</summary>
        /// <remarks>Uses a specific serializer.</remarks>
        /// <exception cref="JsonLD.Core.JsonLdError"></exception>
        public static async Task<JToken> FromRDFAsync(JToken input, JsonLdOptions options, IRDFParser parser
            )
        {
            RDFDataset dataset = parser.Parse(input);
            // convert from RDF
            JToken rval = new JsonLdApi(options).FromRDF(dataset);
            // re-process using the generated context if outputForm is set
            if (options.outputForm != null)
            {
                if ("expanded".Equals(options.outputForm))
                {
                    return rval;
                }
                else
                {
                    if ("compacted".Equals(options.outputForm))
                    {
                        return await CompactAsync(rval, dataset.GetContext(), options).ConfigureAwait(false);
                    }
                    else
                    {
                        if ("flattened".Equals(options.outputForm))
                        {
                            return await FlattenAsync(rval, dataset.GetContext(), options).ConfigureAwait(false);
                        }
                        else
                        {
                            throw new JsonLdError(JsonLdError.Error.UnknownError);
                        }
                    }
                }
            }
            return rval;
        }

        /// <exception cref="JsonLD.Core.JsonLdError"></exception>
        public static Task<JToken> FromRDFAsync(JToken input, IRDFParser parser)
        {
            return FromRDFAsync(input, new JsonLdOptions(string.Empty), parser);
        }

        /// <summary>Outputs the RDF dataset found in the given JSON-LD object.</summary>
        /// <remarks>Outputs the RDF dataset found in the given JSON-LD object.</remarks>
        /// <param name="input">the JSON-LD input.</param>
        /// <param name="callback">
        /// A callback that is called when the input has been converted to
        /// Quads (null to use options.format instead).
        /// </param>
        /// <?></?>
        /// <param name="callback">(err, dataset) called once the operation completes.</param>
        /// <exception cref="JsonLDNet.Core.JsonLdError"></exception>
        public static async Task<object> ToRDFAsync(JToken input, IJSONLDTripleCallback callback, JsonLdOptions
             options)
        {
            JToken expandedInput = await ExpandAsync(input, options).ConfigureAwait(false);
            JsonLdApi api = await JsonLdApi.CreateAsync(expandedInput, options).ConfigureAwait(false);
            RDFDataset dataset = api.ToRDF();
            // generate namespaces from context
            if (options.useNamespaces)
            {
                JArray _input;
                if (input is JArray)
                {
                    _input = (JArray)input;
                }
                else
                {
                    _input = new JArray();
                    _input.Add((JObject)input);
                }
                foreach (JToken e in _input)
                {
                    if (((JObject)e).ContainsKey("@context"))
                    {
                        dataset.ParseContext((JObject)e["@context"]);
                    }
                }
            }
            if (callback != null)
            {
                return callback.Call(dataset);
            }
            if (options.format != null)
            {
                if ("application/nquads".Equals(options.format))
                {
                    return new NQuadTripleCallback().Call(dataset);
                }
                else
                {
                    if ("text/turtle".Equals(options.format))
                    {
                        return new TurtleTripleCallback().Call(dataset);
                    }
                    else
                    {
                        throw new JsonLdError(JsonLdError.Error.UnknownFormat, options.format);
                    }
                }
            }
            return dataset;
        }

        /// <exception cref="JsonLD.Core.JsonLdError"></exception>
        public static Task<object> ToRDFAsync(JToken input, JsonLdOptions options)
        {
            return ToRDFAsync(input, null, options);
        }

        /// <exception cref="JsonLD.Core.JsonLdError"></exception>
        public static object ToRDFAsync(JToken input, IJSONLDTripleCallback callback)
        {
            return ToRDFAsync(input, callback, new JsonLdOptions(string.Empty));
        }

        /// <exception cref="JsonLD.Core.JsonLdError"></exception>
        public static object ToRDFAsync(JToken input)
        {
            return ToRDFAsync(input, new JsonLdOptions(string.Empty));
        }


        /// <summary>Performs RDF dataset normalization on the given JSON-LD input.</summary>
        /// <remarks>
        /// Performs RDF dataset normalization on the given JSON-LD input. The output
        /// is an RDF dataset unless the 'format' option is used.
        /// </remarks>
        /// <param name="input">the JSON-LD input to normalize.</param>
        /// <?></?>
        /// <param name="callback">(err, normalized) called once the operation completes.</param>
        /// <exception cref="JSONLDProcessingError">JSONLDProcessingError</exception>
        /// <exception cref="JsonLDNet.Core.JsonLdError"></exception>
        public static async Task<object> NormalizeAsync(JToken input, JsonLdOptions options)
        {
            JsonLdOptions opts = options.Clone();
            opts.format = null;
            RDFDataset dataset = (RDFDataset)(await ToRDFAsync(input, opts).ConfigureAwait(false));
            return new JsonLdApi(options).Normalize(dataset);
        }

        /// <exception cref="JsonLD.Core.JsonLdError"></exception>
        public static Task<object> NormalizeAsync(JToken input)
        {
            return NormalizeAsync(input, new JsonLdOptions(string.Empty));
        }
    }
}
