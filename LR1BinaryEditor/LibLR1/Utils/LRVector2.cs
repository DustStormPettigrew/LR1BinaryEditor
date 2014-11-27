using System.IO;

namespace LibLR1.Utils {
    public class LRVector2 {
        private float m_X, m_Y;

        public float X { get { return m_X; } set { m_X = value; } }
        public float Y { get { return m_Y; } set { m_Y = value; } }

        public LRVector2()
            : this(0, 0) { }

        public LRVector2(float x, float y) {
            m_X = x;
            m_Y = y;
        }

        public static LRVector2 FromStream(Stream stream) {
            LRVector2 val = new LRVector2();
            val.X = BinaryFileHelper.ReadFloatWithHeader(stream);
            val.Y = BinaryFileHelper.ReadFloatWithHeader(stream);
            return val;
        }

        public static void ToStream(Stream stream, LRVector2 vec) {
            BinaryFileHelper.WriteFloatWithHeader(stream, vec.X);
            BinaryFileHelper.WriteFloatWithHeader(stream, vec.Y);
        }

        public override string ToString() {
            return "(" + X + ", " + Y + ")";
        }
    }
}
