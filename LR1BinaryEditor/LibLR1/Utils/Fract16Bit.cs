using System.IO;

namespace LibLR1.Utils {
    public class Fract16Bit {
        private short m_Value;

        public short Value { get { return m_Value; } set { m_Value = value; } }
        public float AsFloat {
            get { return m_Value / 256f; }
            set { m_Value = (short)(value * 256); }
        }

        public Fract16Bit()
            : this(0f) { }

        public Fract16Bit(float value) {
            AsFloat = value;
        }

        public Fract16Bit(short value) {
            m_Value = value;
        }

        public static Fract16Bit FromStream(Stream stream) {
            return new Fract16Bit(BinaryFileHelper.ReadShort(stream));
        }
    }
}
