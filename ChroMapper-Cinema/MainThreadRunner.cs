using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ChroMapper_Cinema {

public class MainThreadRunner : MonoBehaviour {
	public void Run(UnityAction action) {
		queue.Enqueue(action);
	}
	
	private void Update() {
		while (queue.Count > 0) {
			queue.Dequeue()();
		}
	}
	
	private Queue<UnityAction> queue = new();
}

}
