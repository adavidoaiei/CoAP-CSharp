﻿/*
 * Copyright (c) 2011-2014, Longxiang He <helongxiang@smeshlink.com>,
 * SmeshLink Technology Co.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY.
 * 
 * This file is part of the CoAP.NET, a CoAP framework in C#.
 * Please see README for more information.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Com.AugustCellars.CoAP.EndPoint.Resources;
using Com.AugustCellars.CoAP.Log;
using Com.AugustCellars.CoAP.Server.Resources;
using PeterO.Cbor;

namespace Com.AugustCellars.CoAP
{
    /// <summary>
    /// This class provides link format definitions as specified in
    /// draft-ietf-core-link-format-06
    /// </summary>
    public static class LinkFormat
    {
        /// <summary>
        /// What is the set of attributes that have space separated values.
        /// Being on this list affects not only parsing but serialization as well.
        /// </summary>
        public static string[] SpaceSeparatedValueAttributes = new string[] {
            "rt", "rev", "if", "rel"
        };

        /// <summary>
        /// What is the set of attributes that must appear only once in a link format
        /// </summary>
        public static string[] SingleOccuranceAttributes = new string[] {
            "title",  "sz", "obs"
        };

        /// <summary>
        /// Should the parsing be strict or not.
        /// Enforces the Single Occurance rule.
        /// </summary>
        public static bool ParseStrictMode = false;

        /// <summary>
        /// Name of the attribute Resource Type
        /// </summary>
        public static readonly string ResourceType = "rt";

        /// <summary>
        /// Name of the attribute Interface Description
        /// </summary>
        public static readonly string InterfaceDescription = "if";

        /// <summary>
        /// Name of the attribute Content Type
        /// </summary>
        public static readonly string ContentType = "ct";

        /// <summary>
        /// Name of the attribute Max Size Estimate
        /// </summary>
        public static readonly string MaxSizeEstimate = "sz";

        /// <summary>
        /// Name of the attribute Title
        /// </summary>
        public static readonly string Title = "title";

        /// <summary>
        /// Name of the attribute Observable
        /// </summary>
        public static readonly string Observable = "obs";

        /// <summary>
        /// Name of the attribute link
        /// </summary>
        public static readonly string Link = "href";

        /// <summary>
        /// The string as the delimiter between resources
        /// </summary>
        public static readonly string Delimiter = ",";

        /// <summary>
        /// The string to separate attributes
        /// </summary>
        public static readonly string Separator = ";";

        public static readonly Regex DelimiterRegex = new Regex("\\s*" + Delimiter + "+\\s*");
        public static readonly Regex SeparatorRegex = new Regex("\\s*" + Separator + "+\\s*");

        public static readonly Regex ResourceNameRegex = new Regex("<[^>]*>");
        public static readonly Regex WordRegex = new Regex("\\w+");
        public static readonly Regex QuotedString = new Regex("\\G\".*?\"");
        public static readonly Regex Cardinal = new Regex("\\G\\d+");

        private static readonly ILogger _Log = LogManager.GetLogger(typeof(LinkFormat));

        /// <summary>
        /// Serialize resources starting at a resource node into WebLink format
        /// </summary>
        /// <param name="root">resource to start at</param>
        /// <returns>web link format string</returns>
        public static string Serialize(IResource root)
        {
            return Serialize(root, null);
        }

        /// <summary>
        /// Serialize resources starting at a resource node into WebLink format
        /// </summary>
        /// <param name="root">resource to start at</param>
        /// <param name="queries">queries to filter the serialization</param>
        /// <returns>web link format string</returns>
        public static string Serialize(IResource root, IEnumerable<string> queries)
        {
            StringBuilder linkFormat = new StringBuilder();

            List<string> queryList = null;
            if (queries != null) queryList = queries.ToList();

            if (root.Children != null) {
                foreach (IResource child in root.Children) {
                    SerializeTree(child, queryList, linkFormat);
                }
            }

            if (linkFormat.Length > 1) linkFormat.Remove(linkFormat.Length - 1, 1);

            return linkFormat.ToString();
        }

        public static byte[] SerializeCbor(IResource root, IEnumerable<string> queries)
        {
            CBORObject linkFormat = CBORObject.NewArray();

            List<string> queryList = null;
            if (queries != null) queryList = queries.ToList();

            foreach (IResource child in root.Children) {
                SerializeTree(child, queryList, linkFormat);
            }

            return linkFormat.EncodeToBytes();
        }

        public static IEnumerable<WebLink> Parse(string linkFormat)
        {
            if (string.IsNullOrEmpty(linkFormat)) {
                yield break;
            }


            string[] resources = SplitOn(linkFormat, ',');

            foreach (string resource in resources) {
                string[] attributes = SplitOn(resource, ';');
                if (attributes[0][0] != '<' || attributes[0][attributes[0].Length - 1] != '>') {
                    throw new ArgumentException();
                }
                WebLink link = new WebLink(attributes[0].Substring(1, attributes[0].Length-2));

                for (int i = 1; i < attributes.Length; i++) {
                    int eq = attributes[i].IndexOf('=');
                    string name;
                    name = eq == -1 ? attributes[i] : attributes[i].Substring(0, eq);

                    if (ParseStrictMode && SingleOccuranceAttributes.Contains(name)) {
                        throw new ArgumentException($"'{name}' occurs multiple times");
                    }

                    if (eq == -1) {
                        link.Attributes.Add(name);
                    }
                    else {
                        string value = attributes[i].Substring(eq + 1);
                        if (value[0] == '"') {
                            if (value[value.Length-1] != '"') throw new ArgumentException();
                            value = value.Substring(1, value.Length - 2);
                        }
                        link.Attributes.Set(name, value);
                    }
                }

                yield return link;
            }
        }

        private static void SerializeTree(IResource resource, IReadOnlyCollection<string> queries, StringBuilder sb)
        {
            if (resource.Visible && Matches(resource, queries)) {
                SerializeResource(resource, sb);
                sb.Append(",");
            }

            if (resource.Children == null) return;

            // sort by resource name
            List<IResource> childrens = new List<IResource>(resource.Children);
            childrens.Sort((r1, r2) => string.CompareOrdinal(r1.Name, r2.Name));

            foreach (IResource child in childrens) {
                SerializeTree(child, queries, sb);
            }
        }

        private static void SerializeTree(IResource resource, IReadOnlyCollection<string> queries, CBORObject cbor)
        {
            if (resource.Visible && Matches(resource, queries)) {
                SerializeResource(resource, cbor);
            }
        }

        private static void SerializeResource(IResource resource, StringBuilder sb)
        {
            sb.Append("<")
                .Append(resource.Path)
                .Append(resource.Name)
                .Append(">");
            SerializeAttributes(resource.Attributes, sb);
        }

        private static void SerializeResource(IResource resource, CBORObject cbor)
        {
            CBORObject obj = CBORObject.NewMap();

            obj.Add(1, resource.Path + resource.Name);
            SerializeAttributes(resource.Attributes, obj);
        }

        private static void SerializeAttributes(ResourceAttributes attributes, StringBuilder sb)
        {
            List<string> keys = new List<string>(attributes.Keys);
            keys.Sort();
            foreach (string name in keys) {
                List<string> values = new List<string>(attributes.GetValues(name));
                if (values.Count == 0) continue;
                sb.Append(Separator);
                SerializeAttribute(name, values, sb);
            }
        }

        private static void SerializeAttributes(ResourceAttributes attributes, CBORObject cbor)
        {
            List<string> keys = new List<string>(attributes.Keys);
            keys.Sort();
            foreach (string name in keys) {
                List<string> values = new List<string>(attributes.GetValues(name));
                if (values.Count == 0) continue;
                SerializeAttribute(name, values, cbor);
            }
        }

        private static void SerializeAttribute(string name, IReadOnlyCollection<string> values, StringBuilder sb)
        {
            bool quotes = false;
            bool useSpace = SpaceSeparatedValueAttributes.Contains(name);
            bool first = true;

            foreach (string value in values) {
                if (first || !useSpace) {
                    sb.Append(name);
                }

                if (string.IsNullOrEmpty(value)) {
                    if (!useSpace) sb.Append(';');
                    first = false;
                    continue;
                }

                if (first || !useSpace) {
                    sb.Append('=');
                    if ((useSpace && values.Count > 1) || !IsNumber(value)) {
                        sb.Append('"');
                        quotes = true;
                    }
                }
                else {
                    sb.Append(' ');
                }

                sb.Append(value);

                if (!useSpace) {
                    if (quotes) {
                        sb.Append('"');
                        quotes = false;
                    }
                    sb.Append(';');
                }

                first = false;
            }
            if (quotes) {
                sb.Append('"');
            }

            if (!useSpace) {
                sb.Length = sb.Length - 1;
            }
        }

        private static void SerializeAttribute(string name, IEnumerable<string> values, CBORObject cbor)
        {
            bool quotes = false;
            StringBuilder sb = new StringBuilder();
            bool useSpace = SpaceSeparatedValueAttributes.Contains(name);

            using (IEnumerator<string> it = values.GetEnumerator()) {
                if (!it.MoveNext() || string.IsNullOrEmpty(it.Current)) {
                    cbor.Add(name, CBORObject.True);
                    return;
                }

                string first = it.Current;
                bool more = it.MoveNext();
                if (more || !IsNumber(first)) {
                    sb.Append('"');
                    quotes = true;
                }

                sb.Append(first);
                while (more) {
                    sb.Append(' ');
                    sb.Append(it.Current);
                    more = it.MoveNext();
                }

                if (quotes) sb.Append('"');
            }

            cbor.Add(name, sb.ToString());
        }

        private static bool IsNumber(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (char c in value) {
                if (!char.IsNumber(c)) return false;
            }
            return true;
        }

#if false
        public static string Serialize(Resource resource, IEnumerable<Option> query, bool recursive)
        {
            StringBuilder linkFormat = new StringBuilder();

            // skip hidden and empty root in recursive mode, always skip non-matching resources
            if ((!resource.Hidden && (resource.Name.Length > 0) || !recursive)
                && Matches(resource, query)) {
                linkFormat.Append("<")
                    .Append(resource.Path)
                    .Append(">");

                foreach (LinkAttribute attr in resource.LinkAttributes) {
                    linkFormat.Append(Separator);
                    attr.Serialize(linkFormat);
                }
            }

            if (recursive) {
                foreach (Resource sub in resource.GetSubResources()) {
                    string next = Serialize(sub, query, true);

                    if (next.Length > 0) {
                        if (linkFormat.Length > 3) linkFormat.Append(Delimiter);
                        linkFormat.Append(next);
                    }
                }
            }

            return linkFormat.ToString();
        }
#endif

        public static RemoteResource Deserialize(string linkFormat)
        {
            RemoteResource root = new RemoteResource(string.Empty);
            if (string.IsNullOrEmpty(linkFormat)) {
                return root;
            }

            string[] links = SplitOn(linkFormat, ',');

            foreach (string link in links) {
                string[] attributes = SplitOn(link, ';');
                if (attributes[0][0] != '<' || attributes[0][attributes[0].Length - 1] != '>') {
                    throw new ArgumentException();
                }

                RemoteResource resource = new RemoteResource(attributes[0].Substring(1, attributes[0].Length-2));

                for (int i = 1; i< attributes.Length; i++) {
                    int eq = attributes[i].IndexOf('=');
                    if (eq == -1) {
                        resource.Attributes.Add(attributes[i]);
                    }
                    else {
                        string value = attributes[i].Substring(eq + 1);
                        if (value[0] == '"') {
                            if (value[value.Length - 1] != '"') throw new ArgumentException();
                            value = value.Substring(1, value.Length - 2);
                        }
                        resource.Attributes.Add(attributes[i].Substring(0, eq), value);
                    }
                }

                root.AddSubResource(resource);
            }

#if false
            Scanner scanner = new Scanner(linkFormat);

            string path = null;
            while ((path = scanner.Find(ResourceNameRegex)) != null) {
                path = path.Substring(1, path.Length - 2);

                // Retrieve specified resource, create if necessary
                RemoteResource resource = new RemoteResource(path);

                LinkAttribute attr = null;
                while (scanner.Find(DelimiterRegex, 1) == null && (attr = ParseAttribute(scanner)) != null) {
                    AddAttribute(resource.LinkAttributes, attr);
                }

                root.AddSubResource(resource);
            }
#endif

            return root;
        }

#if false
        private static LinkAttribute ParseAttribute(Scanner scanner)
        {
            string name = scanner.Find(WordRegex);
            if (name == null) return null;
            else {
                object value = null;
                // check for name-value-pair
                if (scanner.Find(new Regex("="), 1) == null)
                    // flag attribute
                    value = true;
                else {
                    string s = null;
                    if ((s = scanner.Find(QuotedString)) != null)
                        // trim " "
                        value = s.Substring(1, s.Length - 2);
                    else if ((s = scanner.Find(Cardinal)) != null) value = int.Parse(s);
                    // TODO what if both pattern failed?
                }
                return new LinkAttribute(name, value);
            }
        }
#endif

#if false
        private static bool Matches(Resource resource, IEnumerable<Option> query)
        {
            if (resource == null) return false;

            if (query == null) return true;

            foreach (Option q in query) {
                string s = q.StringValue;
                int delim = s.IndexOf('=');
                if (delim == -1) {
                    // flag attribute
                    if (resource.GetAttributes(s).Count > 0) return true;
                }
                else {
                    string attrName = s.Substring(0, delim);
                    string expected = s.Substring(delim + 1);

                    if (attrName.Equals(LinkFormat.Link)) {
                        if (expected.EndsWith("*")) return resource.Path.StartsWith(expected.Substring(0, expected.Length - 1));
                        else return resource.Path.Equals(expected);
                    }

                    foreach (LinkAttribute attr in resource.GetAttributes(attrName)) {
                        string actual = attr.Value.ToString();

                        // get prefix length according to "*"
                        int prefixLength = expected.IndexOf('*');
                        if (prefixLength >= 0 && prefixLength < actual.Length) {
                            // reduce to prefixes
                            expected = expected.Substring(0, prefixLength);
                            actual = actual.Substring(0, prefixLength);
                        }

                        // handle case like rt=[Type1 Type2]
                        if (actual.IndexOf(' ') > -1) {
                            foreach (string part in actual.Split(' ')) {
                                if (part.Equals(expected)) return true;
                            }
                        }

                        if (expected.Equals(actual)) return true;
                    }
                }
            }

            return false;
        }
#endif

        private static bool Matches(IResource resource, IReadOnlyCollection<string> query)
        {
            if (resource == null) return false;
            if (query == null) return true;

            using (IEnumerator<string> ie = query.GetEnumerator()) {
                if (!ie.MoveNext()) return true;

                ResourceAttributes attributes = resource.Attributes;
                string path = resource.Path + resource.Name;

                do {
                    string s = ie.Current;

                    int delim = s.IndexOf('=');
                    if (delim == -1) {
                        // flag attribute
                        if (attributes.Contains(s)) return true;
                    }
                    else {
                        string attrName = s.Substring(0, delim);
                        string expected = s.Substring(delim + 1);

                        if (attrName.Equals(LinkFormat.Link)) {
                            if (expected.EndsWith("*")) return path.StartsWith(expected.Substring(0, expected.Length - 1));
                            else return path.Equals(expected);
                        }
                        else if (attributes.Contains(attrName)) {
                            // lookup attribute value
                            foreach (string value in attributes.GetValues(attrName)) {
                                string actual = value;
                                // get prefix length according to "*"
                                int prefixLength = expected.IndexOf('*');
                                if (prefixLength >= 0 && prefixLength < actual.Length) {
                                    // reduce to prefixes
                                    expected = expected.Substring(0, prefixLength);
                                    actual = actual.Substring(0, prefixLength);
                                }

                                // handle case like rt=[Type1 Type2]
                                if (actual.IndexOf(' ') > -1) {
                                    foreach (string part in actual.Split(' ')) {
                                        if (part.Equals(expected)) return true;
                                    }
                                }

                                if (expected.Equals(actual)) return true;
                            }
                        }
                    }
                } while (ie.MoveNext());
            }

            return false;
        }

        internal static bool AddAttribute(ICollection<LinkAttribute> attributes, LinkAttribute attrToAdd)
        {
            if (IsSingle(attrToAdd.Name)) {
                foreach (LinkAttribute attr in attributes) {
                    if (attr.Name.Equals(attrToAdd.Name)) {
                        if (_Log.IsDebugEnabled) _Log.Debug("Found existing singleton attribute: " + attr.Name);
                        return false;
                    }
                }
            }

            // special rules
            if (attrToAdd.Name.Equals(ContentType) && attrToAdd.IntValue < 0) return false;
            if (attrToAdd.Name.Equals(MaxSizeEstimate) && attrToAdd.IntValue < 0) return false;

            attributes.Add(attrToAdd);
            return true;
        }

        private static bool IsSingle(string name)
        {
            return SingleOccuranceAttributes.Contains(name);
        }

        private static string quoteChars = "'\"";

        private static string[] SplitOn(string input, char splitChar)
        {
            bool escape = false;
            char inString = (char) 0;
            List<string> output = new List<string>();
            int startChar = 0;
            

            for (int i = 0; i < input.Length; i++) {
                char c = input[i];
                if (c == '\\') {
                    escape = !escape;
                    continue;
                }

                if (c == splitChar) {
                    if (inString == 0) {
                        output.Add(input.Substring(startChar, i - startChar));
                        startChar = i + 1;
                    }
                }
                else if (quoteChars.IndexOf(c) > -1 && !escape) {
                    if (c == inString) inString = (char) 0;
                    else if (inString == 0) inString = c;
                }
            }

            if (inString != 0) throw new ArgumentException();
            if (startChar < input.Length) output.Add(input.Substring(startChar));

            return output.ToArray();
        }
    }
}
