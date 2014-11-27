using System.IO;

namespace LibLR1.Utils {
    public class LRQuaternion {
        private float m_X, m_Y, m_Z, m_W;

        public float X { get { return m_X; } set { m_X = value; } }
        public float Y { get { return m_Y; } set { m_Y = value; } }
        public float Z { get { return m_Z; } set { m_Z = value; } }
        public float W { get { return m_W; } set { m_W = value; } }

        public LRQuaternion()
            : this(0, 0, 0, 1) { }

        public LRQuaternion(float x, float y, float z, float w) {
            m_X = x;
            m_Y = y;
            m_Z = z;
            m_W = w;
        }

        public static LRQuaternion FromStream(Stream stream) {
            LRQuaternion val = new LRQuaternion();
            val.X = BinaryFileHelper.ReadFloatWithHeader(stream);
            val.Y = BinaryFileHelper.ReadFloatWithHeader(stream);
            val.Z = BinaryFileHelper.ReadFloatWithHeader(stream);
            val.W = BinaryFileHelper.ReadFloatWithHeader(stream);
            return val;
        }

        public static void ToStream(Stream stream, LRQuaternion quaternion) {
            BinaryFileHelper.WriteFloatWithHeader(stream, quaternion.X);
            BinaryFileHelper.WriteFloatWithHeader(stream, quaternion.Y);
            BinaryFileHelper.WriteFloatWithHeader(stream, quaternion.Z);
            BinaryFileHelper.WriteFloatWithHeader(stream, quaternion.W);
        }
    }
}
