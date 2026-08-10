#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MgDataKit.Editor {
    [Serializable]
    public sealed class MgDataKitLarkTableNode {
        [SerializeField]
        private string _title;

        [SerializeField]
        private string _nodeToken;

        [SerializeField]
        private string _parentNodeToken;

        [SerializeField]
        private string _objectToken;

        [SerializeField]
        private string _objectType;

        [SerializeField]
        private string _nodeType;

        [SerializeField]
        private bool _hasChildren;

        [SerializeField]
        private int _depth;

        [SerializeField]
        private string _source;

        [SerializeField]
        private string _sheetId;

        [SerializeField]
        private string _sheetName;

        [SerializeField]
        private bool _isWorkbookSheet;

        public string Title => _title;
        public string NodeToken => _nodeToken;
        public string ParentNodeToken => _parentNodeToken;
        public string ObjectToken => _objectToken;
        public string ObjectType => _objectType;
        public string NodeType => _nodeType;
        public bool HasChildren => _hasChildren;
        public int Depth => _depth;
        public string SheetId => _sheetId;
        public string SheetName => _sheetName;
        public bool IsWorkbookSheet => _isWorkbookSheet;
        public bool IsSheet => !_isWorkbookSheet &&
                               string.Equals(_objectType, "sheet", StringComparison.OrdinalIgnoreCase);
        public string Source => string.IsNullOrWhiteSpace(_source)
            ? (string.IsNullOrWhiteSpace(_nodeToken) ? string.Empty : "wiki/" + _nodeToken)
            : _source;

        private MgDataKitLarkTableNode() {
        }

        internal MgDataKitLarkTableNode(MgDataLarkWikiNodeInfo node) {
            _title = node.Title;
            _nodeToken = node.NodeToken;
            _parentNodeToken = node.ParentNodeToken;
            _objectToken = node.ObjectToken;
            _objectType = node.ObjectType;
            _nodeType = node.NodeType;
            _hasChildren = node.HasChildren;
            _depth = node.Depth;
            _source = node.Source;
            _sheetId = node.SheetId;
            _sheetName = node.SheetName;
            _isWorkbookSheet = node.IsWorkbookSheet;
        }
    }

    /// <summary>
    /// MgDataKit 飞书知识库目录快照。节点按深度优先顺序存储，Depth 与 ParentNodeToken 保留树结构。
    /// </summary>
    public sealed class MgDataKitLarkTableCatalog : ScriptableObject {
        [SerializeField]
        private string _wikiSpaceId;

        [SerializeField]
        private string _rootNodeToken;

        [SerializeField]
        private List<MgDataKitLarkTableNode> _nodes = new();

        public string WikiSpaceId => _wikiSpaceId;
        public string RootNodeToken => _rootNodeToken;
        public IReadOnlyList<MgDataKitLarkTableNode> Nodes => MutableNodes;

        public int SheetCount {
            get {
                var count = 0;
                List<MgDataKitLarkTableNode> nodes = MutableNodes;
                for (var i = 0; i < nodes.Count; i++) {
                    if (nodes[i] != null && nodes[i].IsSheet)
                        count++;
                }

                return count;
            }
        }

        public int WorkbookSheetCount {
            get {
                var count = 0;
                List<MgDataKitLarkTableNode> nodes = MutableNodes;
                for (var i = 0; i < nodes.Count; i++) {
                    if (nodes[i] != null && nodes[i].IsWorkbookSheet)
                        count++;
                }

                return count;
            }
        }

        internal void Replace(
            string wikiSpaceId,
            string rootNodeToken,
            IReadOnlyList<MgDataLarkWikiNodeInfo> nodes) {
            _wikiSpaceId = wikiSpaceId;
            _rootNodeToken = rootNodeToken;
            List<MgDataKitLarkTableNode> catalogNodes = MutableNodes;
            catalogNodes.Clear();
            if (nodes == null)
                return;

            for (var i = 0; i < nodes.Count; i++)
                catalogNodes.Add(new MgDataKitLarkTableNode(nodes[i]));
        }

        private List<MgDataKitLarkTableNode> MutableNodes =>
            _nodes ??= new List<MgDataKitLarkTableNode>();
    }
}
#endif
