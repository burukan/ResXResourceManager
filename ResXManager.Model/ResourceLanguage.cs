﻿namespace tomenglertde.ResXManager.Model
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Windows.Threading;
    using System.Xml;
    using System.Xml.Linq;

    using JetBrains.Annotations;

    using Throttle;

    using tomenglertde.ResXManager.Infrastructure;
    using tomenglertde.ResXManager.Model.Properties;

    using TomsToolbox.Essentials;
    using TomsToolbox.Wpf;

    /// <summary>
    /// Represents a set of localized resources.
    /// </summary>
    [Localizable(false)]
    public class ResourceLanguage
    {
        [NotNull]
        private const string Quote = "\"";
        [NotNull]
        private const string WinFormsMemberNamePrefix = @">>";
        [NotNull]
        private static readonly XName _spaceAttributeName = XNamespace.Xml.GetName(@"space");
        [NotNull]
        private static readonly XName _typeAttributeName = XNamespace.None.GetName(@"type");
        [NotNull]
        private static readonly XName _mimetypeAttributeName = XNamespace.None.GetName(@"mimetype");
        [NotNull]
        private static readonly XName _nameAttributeName = XNamespace.None.GetName(@"name");

        [NotNull]
        private readonly XDocument _document;

        [NotNull]
        // ReSharper disable once AssignNullToNotNullAttribute
        private XElement DocumentRoot => _document.Root;

        [NotNull]
        private IDictionary<string, Node> _nodes = new Dictionary<string, Node>();

        [NotNull]
        private readonly XName _dataNodeName;
        [NotNull]
        private readonly XName _valueNodeName;
        [NotNull]
        private readonly XName _commentNodeName;

        [NotNull]
        private readonly IConfiguration _configuration;

        private bool _hasUncommittedChanges;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceLanguage" /> class.
        /// </summary>
        /// <param name="container">The containing resource entity.</param>
        /// <param name="cultureKey">The culture key.</param>
        /// <param name="file">The .resx file having all the localization.</param>
        /// <exception cref="System.InvalidOperationException">
        /// </exception>
        internal ResourceLanguage([NotNull] ResourceEntity container, [NotNull] CultureKey cultureKey, [NotNull] ProjectFile file)
        {
            Container = container;
            CultureKey = cultureKey;
            ProjectFile = file;
            _configuration = container.Container.Configuration;

            try
            {
                _document = file.Load();
            }
            catch (XmlException ex)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Resources.InvalidResourceFileError, file.FilePath), ex);
            }

            if (DocumentRoot == null)
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Resources.InvalidResourceFileError, file.FilePath));

            var defaultNamespace = DocumentRoot.GetDefaultNamespace();

            _dataNodeName = defaultNamespace.GetName(@"data");
            _valueNodeName = defaultNamespace.GetName(@"value");
            _commentNodeName = defaultNamespace.GetName(@"comment");

            UpdateNodes();
        }

        private void UpdateNodes()
        {
            var data = DocumentRoot.Elements(_dataNodeName);

            var elements = data
                .Where(IsStringType)
                .Select(item => new Node(this, item))
                .Where(item => !item.Key.StartsWith(WinFormsMemberNamePrefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (_configuration.DuplicateKeyHandling == DuplicateKeyHandling.Rename)
            {
                MakeKeysValid(elements);
            }
            else
            {
                if (elements.Any(item => string.IsNullOrEmpty(item.Key)))
                    throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Resources.EmptyKeysError, ProjectFile.FilePath));
            }

            try
            {
                _nodes = elements.ToDictionary(item => item.Key);
            }
            catch (ArgumentException ex)
            {
                var duplicateKeys = string.Join(@", ", elements.GroupBy(item => item.Key).Where(group => group.Count() > 1).Select(group => Quote + group.Key + Quote));
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Resources.DuplicateKeyError, ProjectFile.FilePath, duplicateKeys), ex);
            }
        }

        /// <summary>
        /// Gets the culture of this language.
        /// </summary>
        [CanBeNull]
        public CultureInfo Culture => CultureKey.Culture;

        /// <summary>
        /// Gets the display name of this language.
        /// </summary>
        [NotNull]
        public string DisplayName => ToString();

        /// <summary>
        /// Gets all the resource keys defined in this language.
        /// </summary>
        [NotNull, ItemNotNull]
        public IEnumerable<string> ResourceKeys => _nodes.Keys;

        public bool HasChanges
        {
            get
            {
                if (_hasUncommittedChanges)
                    CommitChanges();

                return ProjectFile.HasChanges;
            }
        }

        public bool IsSaving { get; private set; }

        [NotNull]
        public string FileName => ProjectFile.FilePath;

        [NotNull]
        public ProjectFile ProjectFile { get; }

        public bool IsNeutralLanguage => Container.Languages.FirstOrDefault() == this;

        [NotNull]
        public CultureKey CultureKey { get; }

        [NotNull]
        public ResourceEntity Container { get; }

        private static bool IsStringType([NotNull] XElement entry)
        {
            var typeAttribute = entry.Attribute(_typeAttributeName);

            if (typeAttribute != null)
            {
                return string.IsNullOrEmpty(typeAttribute.Value) || typeAttribute.Value.StartsWith(typeof(string).Name, StringComparison.OrdinalIgnoreCase);
            }

            var mimeTypeAttribute = entry.Attribute(_mimetypeAttributeName);

            return mimeTypeAttribute == null;
        }

        [CanBeNull]
        internal string GetValue([NotNull] string key)
        {
            return !_nodes.TryGetValue(key, out var node) ? null : node?.Text;
        }

        internal bool SetValue([NotNull] string key, [CanBeNull] string value)
        {
            return GetValue(key) == value || SetNodeData(key, node => node.Text = value);
        }

        public void ForceValue([NotNull] string key, [CanBeNull] string value)
        {
            SetNodeData(key, node => node.Text = value);
        }

        private void OnChanged()
        {
            _hasUncommittedChanges = true;

            DeferredCommitChanges();
        }

        [Throttled(typeof(DispatcherThrottle))]
        private void DeferredCommitChanges()
        {
            CommitChanges();
        }

        private void CommitChanges()
        {
            _hasUncommittedChanges = false;

            ProjectFile.Changed(_document, _configuration.SaveFilesImmediatelyUponChange);

            Container.Container.OnLanguageChanged(this);
        }

        internal bool CanEdit()
        {
            return Container.CanEdit(CultureKey);
        }

        /// <summary>
        /// Saves this instance to the resource file.
        /// </summary>
        /// <exception cref="IOException"></exception>
        /// <exception cref="UnauthorizedAccessException"></exception>
        public void Save()
        {
            try
            {
                IsSaving = true;

                ProjectFile.Save(_document);

                Container.Container.OnProjectFileSaved(this, ProjectFile);
            }
            finally
            {
                IsSaving = false;
            }
        }

        public void SortNodes(StringComparison stringComparison)
        {
            if (!SortDocument(stringComparison))
                return;

            UpdateNodes();
            Container.OnItemOrderChanged(this);

            ProjectFile.Changed(_document, true);

            Save();
        }

        private bool SortDocument(StringComparison stringComparison)
        {
            return SortAndAdd(stringComparison, null);
        }

        private bool SortAndAdd(StringComparison stringComparison, [CanBeNull] XElement newNode)
        {
            var comparer = new DelegateComparer<string>((left, right) => string.Compare(left, right, stringComparison));
            string GetName(XElement node) => node.Attribute(_nameAttributeName)?.Value.TrimStart('>') ?? string.Empty;

            var nodes = DocumentRoot
                .Elements(_dataNodeName)
                .ToArray();

            var sortedNodes = nodes
                .OrderBy(GetName, comparer)
                .ToArray();

            var hasContentChanged = SortNodes(nodes, sortedNodes);

            if (newNode == null)
                return hasContentChanged;

            var newNodeName = GetName(newNode);
            var nextNode = sortedNodes.FirstOrDefault(node => comparer.Compare(GetName(node), newNodeName) > 0);

            if (nextNode != null)
            {
                nextNode.AddBeforeSelf(newNode);
            }
            else
            {
                DocumentRoot.Add(newNode);
            }

            return true;
        }

        private bool SortNodes([NotNull, ItemNotNull] XElement[] nodes, [NotNull, ItemNotNull] XElement[] sortedNodes)
        {
            if (nodes.SequenceEqual(sortedNodes))
                return false;

            foreach (var item in nodes)
            {
                item.Remove();
            }

            foreach (var item in sortedNodes)
            {
                DocumentRoot.Add(item);
            }

            return true;
        }

        [CanBeNull]
        internal string GetComment([NotNull] string key)
        {
            if (!_nodes.TryGetValue(key, out var node) || (node == null))
                return null;

            return node.Comment;
        }

        internal bool SetComment([NotNull] string key, [CanBeNull] string value)
        {
            if (GetComment(key) == value)
                return true;

            return SetNodeData(key, node => node.Comment = value);
        }

        private bool SetNodeData([NotNull] string key, [NotNull] Action<Node> updateCallback)
        {
            if (!CanEdit())
                return false;

            try
            {
                if (!_nodes.TryGetValue(key, out var node) || (node == null))
                {
                    node = CreateNode(key);
                }

                updateCallback(node);

                if (!IsNeutralLanguage)
                {
                    if (_configuration.RemoveEmptyEntries && string.IsNullOrEmpty(node.Text) && string.IsNullOrEmpty(node.Comment))
                    {
                        node.Element.Remove();
                        _nodes.Remove(key);
                    }
                }

                OnChanged();
                return true;
            }
            catch (Exception ex)
            {
                var message = string.Format(CultureInfo.CurrentCulture, Resources.FileSaveError, ProjectFile.FilePath, ex.Message);
                throw new IOException(message, ex);
            }
        }

        [NotNull]
        private Node CreateNode([NotNull] string key)
        {
            var content = new XElement(_valueNodeName);
            content.Add(new XText(string.Empty));

            var entry = new XElement(_dataNodeName, new XAttribute(_nameAttributeName, key), new XAttribute(_spaceAttributeName, @"preserve"));
            entry.Add(content, new XText("\n  "));

            var fileContentSorting = _configuration.EffectiveResXSortingComparison;

            if (fileContentSorting.HasValue)
            {
                SortAndAdd(fileContentSorting.Value, entry);
            }
            else
            {
                DocumentRoot.Add(entry);
            }

            UpdateNodes();

            Dispatcher.CurrentDispatcher.BeginInvoke(() => Container.OnItemOrderChanged(this));

            return _nodes[key];
        }

        internal bool RenameKey([NotNull] string oldKey, [NotNull] string newKey)
        {
            if (!CanEdit())
                return false;

            if (!_nodes.TryGetValue(oldKey, out var node) || (node == null))
                return false;

            if (_nodes.ContainsKey(newKey))
                return false;

            _nodes.Remove(oldKey);
            node.Key = newKey;
            _nodes.Add(newKey, node);

            OnChanged();
            return true;
        }

        internal bool RemoveKey([NotNull] string key)
        {
            if (!CanEdit())
                return false;

            try
            {
                if (!_nodes.TryGetValue(key, out var node) || (node == null))
                {
                    return false;
                }

                node.Element.Remove();
                _nodes.Remove(key);

                OnChanged();
                return true;
            }
            catch (Exception ex)
            {
                var message = string.Format(CultureInfo.CurrentCulture, Resources.FileSaveError, ProjectFile.FilePath, ex.Message);
                throw new IOException(message, ex);
            }
        }

        internal bool KeyExists([NotNull] string key)
        {
            return _nodes.ContainsKey(key);
        }

        internal void MoveNode([NotNull] ResourceTableEntry resourceTableEntry, [NotNull][ItemNotNull] IEnumerable<ResourceTableEntry> previousEntries)
        {
            if (!CanEdit())
                return;

            var node = _nodes.GetValueOrDefault(resourceTableEntry.Key);

            if (node == null)
                return;

            var prevousNode = previousEntries
                .Select(entry => _nodes.GetValueOrDefault(entry.Key))
                .FirstOrDefault(item => item != null);

            if (prevousNode == null)
                return;

            var element = node.Element;
            element.Remove();
            prevousNode.Element.AddAfterSelf(element);

            OnChanged();
        }

        internal bool IsContentEqual([NotNull] ResourceLanguage other)
        {
            return _document.ToString(SaveOptions.DisableFormatting) == other._document.ToString(SaveOptions.DisableFormatting);
        }

        private static void MakeKeysValid([NotNull][ItemNotNull] ICollection<Node> elements)
        {
            RenameEmptyKeys(elements);

            RenameDuplicates(elements);
        }

        private static void RenameDuplicates([NotNull, ItemNotNull] ICollection<Node> elements)
        {
            var itemsWithDuplicateKeys = elements.GroupBy(item => item.Key)
                .Where(group => group.Count() > 1);

            foreach (var duplicates in itemsWithDuplicateKeys)
            {
                var index = 1;

                duplicates.Skip(1).ForEach(item => item.Key = GenerateUniqueKey(elements, item, "Duplicate", ref index));
            }
        }

        private static void RenameEmptyKeys([NotNull, ItemNotNull] ICollection<Node> elements)
        {
            var itemsWithEmptyKeys = elements.Where(item => string.IsNullOrEmpty(item.Key));

            var index = 1;

            itemsWithEmptyKeys.ForEach(item => item.Key = GenerateUniqueKey(elements, item, "Empty", ref index));
        }

        [NotNull]
        private static string GenerateUniqueKey([NotNull][ItemNotNull] ICollection<Node> elements, [NotNull] Node item, [CanBeNull] string text, ref int index)
        {
            var key = item.Key;
            string newKey;

            do
            {
                newKey = string.Format(CultureInfo.InvariantCulture, "{0}_{1}[{2}]", key, text, index);
                index += 1;
            }
            while (elements.Any(element => element.Key.Equals(newKey, StringComparison.OrdinalIgnoreCase)));

            return newKey;
        }

        public override string ToString()
        {
            return Culture?.DisplayName ?? Resources.Neutral;
        }

        private class Node
        {
            [NotNull]
            private readonly ResourceLanguage _owner;

            [CanBeNull]
            private string _text;
            [CanBeNull]
            private string _comment;

            public Node([NotNull] ResourceLanguage owner, [NotNull] XElement element)
            {
                Element = element;
                _owner = owner;
            }

            [NotNull]
            public XElement Element { get; }

            [NotNull]
            public string Key
            {
                get => GetNameAttribute(Element).Value;
                set => GetNameAttribute(Element).Value = value;
            }

            [CanBeNull]
            public string Text
            {
                get => _text ?? (_text = LoadText());
                set
                {
                    _text = value ?? string.Empty;

                    var entry = Element;

                    var valueElement = entry.Element(_owner._valueNodeName);
                    if (valueElement == null)
                        throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Resources.InvalidResourceFileValueAttributeMissingError, _owner.FileName));

                    if (valueElement.FirstNode == null)
                    {
                        valueElement.Add(value);
                    }
                    else
                    {
                        valueElement.FirstNode.ReplaceWith(value);
                    }
                }
            }

            [CanBeNull]
            public string Comment
            {
                get => _comment ?? (_comment = LoadComment());
                set
                {
                    _comment = value ?? string.Empty;

                    var entry = Element;

                    var valueElement = entry.Element(_owner._commentNodeName);

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        valueElement?.Remove();
                    }
                    else
                    {
                        if (valueElement == null)
                        {
                            valueElement = new XElement(_owner._commentNodeName);
                            entry.Add(new XText("  "), valueElement, new XText("\n  "));
                        }

                        if (!(valueElement.FirstNode is XText textNode))
                        {
                            textNode = new XText(value);
                            valueElement.Add(textNode);
                        }
                        else
                        {
                            textNode.Value = value;
                        }
                    }
                }
            }

            [CanBeNull]
            private string LoadText()
            {
                var entry = Element;

                var valueElement = entry.Element(_owner._valueNodeName);
                if (valueElement == null)
                {
                    throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Resources.InvalidResourceFileValueAttributeMissingError, _owner.FileName));
                }

                return !(valueElement.FirstNode is XText textNode) ? string.Empty : textNode.Value;
            }

            [CanBeNull]
            private string LoadComment()
            {
                var entry = Element;

                var valueElement = entry.Element(_owner._commentNodeName);
                if (valueElement == null)
                    return string.Empty;

                return !(valueElement.FirstNode is XText textNode) ? string.Empty : textNode.Value;
            }

            [NotNull]
            private XAttribute GetNameAttribute([NotNull] XElement entry)
            {
                var nameAttribute = entry.Attribute(_nameAttributeName);
                if (nameAttribute == null)
                {
                    throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Resources.InvalidResourceFileNameAttributeMissingError, _owner.ProjectFile.FilePath));
                }

                return nameAttribute;
            }
        }
    }
}