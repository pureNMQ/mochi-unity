using System;
using System.Runtime.CompilerServices;

namespace Mochi.Unity.Preview.GUI
{
    public readonly struct ModalHandle : IEquatable<ModalHandle>
    {
        private readonly ModalContainer container;
        private readonly int version;

        internal ModalHandle(ModalContainer owner, int handleVersion)
        {
            container = owner;
            version = handleVersion;
        }

        public bool IsValid => container != null && container.ContainsHandle(this);

        internal ModalContainer Container => container;
        internal int Version => version;

        public bool Equals(ModalHandle other)
        {
            return ReferenceEquals(container, other.container) && version == other.version;
        }

        public override bool Equals(object obj)
        {
            return obj is ModalHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((container == null ? 0 : RuntimeHelpers.GetHashCode(container)) * 397) ^ version;
            }
        }

        public static bool operator ==(ModalHandle left, ModalHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ModalHandle left, ModalHandle right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return IsValid ? $"ModalHandle({version})" : "ModalHandle(Invalid)";
        }
    }
}
