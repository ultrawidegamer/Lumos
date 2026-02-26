using System;
using System.Collections.Generic;
using UnityEditor;

namespace LightBakingResoLink {
    public static class TaskQueue {
        private static readonly Queue<Action> taskQueue = new Queue<Action>();
        private static bool isRunning = false;

        public static void Enqueue(Action action) {
            if (action == null) return;
            taskQueue.Enqueue(action);
            if (!isRunning) {
                isRunning = true;
                EditorApplication.update += ProcessQueue;
            }
        }

        private static void ProcessQueue() {
            if (taskQueue.Count == 0) {
                isRunning = false;
                EditorApplication.update -= ProcessQueue;
                return;
            }

            Action action = taskQueue.Dequeue();
            action?.Invoke();
        }
    }
}