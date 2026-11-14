using System;

namespace Island
{
    // Bounded handoff for replaceable state, never for commands/acknowledgements.
    // Producers can replace an obsolete snapshot while the UI is busy.
    public sealed class UiLatest<T> : IDisposable
    {
        private readonly object gate = new object();
        private readonly Action<Action> dispatch;
        private readonly Action<T> apply;
        private T latest;
        private bool queued, closed;
        public UiLatest(Action<Action> send, Action<T> consume) { dispatch = send; apply = consume; }
        public void Post(T value)
        {
            lock (gate) { if (closed) return; latest = value; if (queued) return; queued = true; }
            dispatch(Drain);
        }
        private void Drain()
        {
            T value;
            lock (gate) { if (closed) return; value = latest; latest = default(T); queued = false; }
            apply(value);
        }
        public void Dispose() { lock (gate) { closed = true; latest = default(T); } }
    }
}
