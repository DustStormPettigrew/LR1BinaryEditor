using System.IO;

namespace LibLR1.Utils {
    public class LRVector3 {
        private float m_X, m_Y, m_Z;

        public float X { get { return m_X; } set { m_X = value; } }
        public float Y { get { return m_Y; } set { m_Y = value; } }
        public float Z { get { return m_Z; } set { m_Z = value; } }

        public LRVector3()
            : this(0, 0, 0) { }

        public LRVector3(float x, float y, float z) {
            m_X = x;
            m_Y = y;
            m_Z = z;
        }

        public static LRVector3 FromStream(Stream stream) {
            LRVector3 val = new LRVector3();
            val.X = BinaryFileHelper.ReadFloatWithHeader(stream);
            val.Y = BinaryFileHelper.ReadFloatWithHeader(stream);
            val.Z = BinaryFileHelper.ReadFloatWithHeader(stream);
            return val;
        }

        public static void ToStream(Stream stream, LRVector3 vec) {
            BinaryFileHelper.WriteFloatWithHeader(stream, vec.X);
            BinaryFileHelper.WriteFloatWithHeader(stream, vec.Y);
            BinaryFileHelper.WriteFloatWithHeader(stream, vec.Z);
        }

        public override string ToString() {
            return "(" + X + ", " + Y + ", " + Z + ")";
        }
    }
}
