﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace BehaviorTreeSample
{
    public enum BehaviorStatus
    {
        Inactive,
        Sucess,
        Failure,
        Running,
        Completed,
    }

    /// <summary>
    /// Behavior Treeをシンプルに実装する
    /// </summary>
    public class SimpleBehaviorTree
    {
        /// <summary>
        /// 再評価する際の諸々の情報を格納する
        /// </summary>
        public class ConditionalReevaluate
        {
            public int Index { get; set; }
            public BehaviorStatus Status { get; set; }
            public int CompositeIndex { get; set; }
            public int StackIndex { get; set; }
            public void Initialize(int i, BehaviorStatus status, int stack, int composite)
            {
                Index = i;
                Status = status;
                StackIndex = stack;
                CompositeIndex = composite;
            }
        }

        // Behavior Treeを保持しているオーナーオブジェクト
        private GameObject _owner;
        private Node _rootNode;

        private List<Node> _nodeList = new List<Node>();
        private List<ConditionalReevaluate> _reevaluateList = new List<ConditionalReevaluate>();

        // 現在実行中の枝のスタックを保持する
        private Stack<int> _activeStack = new Stack<int>();

        private bool _isCompleted = false;

        private int _activeNodeIndex = -1;

        // コンストラクタ
        public SimpleBehaviorTree(GameObject owner)
        {
            _owner = owner;
        }

        /// <summary>
        /// アクティブなノードとしてスタックにPushする
        /// </summary>
        /// <param name="node">追加するノード</param>
        private void PushNode(Node node)
        {
            if (_activeStack.Count == 0 || _activeStack.Peek() != node.Index)
            {
                _activeStack.Push(node.Index);
                _activeNodeIndex = _activeStack.Peek();

                //Debug.LogFormat("PushNode: {0}", node.Index);
            }
        }

        /// <summary>
        /// ノードをスタックから取り除く
        /// </summary>
        private void PopNode()
        {
            int popedIndex = _activeStack.Pop();
            _activeNodeIndex = _activeStack.Peek();

            //Debug.LogFormat("PopNode: {0}", popedIndex);
        }

        /// <summary>
        /// 指定したふたつのノードIDのノードの共通の祖先を見つける
        /// </summary>
        /// <param name="node1">ノード1のID</param>
        /// <param name="node2">ノード2のID</param>
        /// <returns>見つかった祖先ノードID</returns>
        private int CommonAncestorNode(int node1, int node2)
        {
            HashSet<int> parentNodes = new HashSet<int>();

            // 再帰的に祖先のIndexをリスト化する
            Node parent1 = _nodeList[node1].ParentNode;
            while (parent1 != null)
            {
                parentNodes.Add(parent1.Index);
                parent1 = parent1.ParentNode;
            }

            Node parent2 = _nodeList[node2].ParentNode;
            int num = parent2.Index;
            while (!parentNodes.Contains(num))
            {
                parent2 = parent2.ParentNode;
                num = parent2.Index;
            }

            return num;
        }

        /// <summary>
        /// 対象ノードを起動（Awake）する
        /// </summary>
        /// <param name="node">起動するノード</param>
        private void CallOnAwake(Node node)
        {
            // ノードに全体のグラフの通し番号を設定する
            node.Index = _nodeList.Count;

            _nodeList.Add(node);

            // 対象ノードにオーナーを設定する
            node.Owner = _owner;

            // ノード起動
            node.OnAwake();

            // CompositeNodeの場合は再帰的に起動させる
            CompositeNode cnode = node as CompositeNode;
            if (cnode != null)
            {
                foreach (var child in cnode.Children)
                {
                    CallOnAwake(child);
                }
            }
        }

        /// <summary>
        /// 再評価が必要なノードを再評価する
        /// </summary>
        /// <returns>再評価して変化のあったノードのIndex</returns>
        private int ReevaluateConditionalTasks()
        {
            for (int i = 0; i < _reevaluateList.Count; i++)
            {
                ConditionalReevaluate cr = _reevaluateList[i];
                BehaviorStatus status = _nodeList[cr.Index].OnUpdate();

                // 前回の状態と変化していたら、祖先まで遡って処理を停止する
                if (cr.Status != status)
                {
                    CompositeNode cnode = _nodeList[cr.CompositeIndex] as CompositeNode;
                    if (cnode != null)
                    {
                        cnode.OnConditionalAbort(cr.Index);
                    }
                    _reevaluateList.Remove(cr);
                    return cr.Index;
                }
            }

            return -1;
        }

        /// <summary>
        /// 再帰的にノードを実行する
        /// </summary>
        /// <param name="node">実行するノード</param>
        private BehaviorStatus Execute(Node node)
        {
            // 実行中のノードをActive Stackに積む
            PushNode(node);

            // Compositeノードの場合は再帰処理
            if (node is CompositeNode)
            {
                CompositeNode cnode = node as CompositeNode;

                while (cnode.CanExecute())
                {
                    Node child = cnode.Children[cnode.CurrentChildIndex];
                    BehaviorStatus childStatus = Execute(child);

                    // 再評価が必要な場合はそれをリストに追加
                    if (cnode.NeedsConditionalAbort)
                    {
                        if (child is ConditionalNode)
                        {
                            _reevaluateList.Add(new ConditionalReevaluate
                            {
                                Index = child.Index,
                                CompositeIndex = cnode.Index,
                                Status = childStatus,
                            });
                        }
                    }

                    if (childStatus == BehaviorStatus.Running)
                    {
                        return BehaviorStatus.Running;
                    }

                    cnode.OnChildExecuted(childStatus);
                }

                if (cnode.Status != BehaviorStatus.Running)
                {
                    // 実行が終了したノードをスタックから外す
                    cnode.OnEnd();
                    PopNode();
                }

                return cnode.Status;
            }
            else
            {
                BehaviorStatus status = node.OnUpdate();
                if (status != BehaviorStatus.Running)
                {
                    node.OnEnd();
                    PopNode();
                }

                return status;
            }
        }

        /// <summary>
        /// ルートノードを設定する
        /// </summary>
        /// <param name="node">ルートノードとして設定するノード</param>
        public void SetRootNode(Node node)
        {
            _rootNode = node;
        }

        /// <summary>
        /// ツリーを開始
        /// </summary>
        public void Start()
        {
            Debug.Log("Tree start.");

            CallOnAwake(_rootNode);
        }

        /// <summary>
        /// ツリーの状態をUpdate
        /// </summary>
        public void Update()
        {
            if (_isCompleted)
            {
                return;
            }

            int abortIndex = ReevaluateConditionalTasks();
            if (abortIndex != -1)
            {
                // 中断したノードと現在実行中のノードの共通祖先を見つける
                int caIndex = CommonAncestorNode(abortIndex, _activeNodeIndex);

                Node activeNode = _nodeList[_activeNodeIndex];
                activeNode.OnAbort();

                while (_activeStack.Count != 0)
                {
                    PopNode();
                    activeNode = _nodeList[_activeNodeIndex];
                    activeNode.OnAbort();

                    if (_activeNodeIndex == caIndex)
                    {
                        break;
                    }

                    ConditionalReevaluate cr = _reevaluateList.FirstOrDefault(r => r.Index == _activeNodeIndex);
                    if (cr != null)
                    {
                        _reevaluateList.Remove(cr);
                    }
                }
            }

            BehaviorStatus status = BehaviorStatus.Inactive;
            if (_activeNodeIndex == -1)
            {
                status = Execute(_rootNode);
            }
            else
            {
                Node node = _nodeList[_activeNodeIndex];
                status = Execute(node);
            }

            if (status == BehaviorStatus.Completed)
            {
                Debug.Log("Behavior Tree has been completed.");

                _isCompleted = true;
            }
        }
    }
}
