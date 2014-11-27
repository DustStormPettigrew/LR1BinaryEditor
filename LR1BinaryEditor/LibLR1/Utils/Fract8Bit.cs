using System.IO;

namespace LibLR1.Utils {
    public class Fract8Bit {
        private sbyte m_Value;

        public sbyte Value { get { return m_Value; } set { m_Value = value; } }
        public float AsFloat {
            get { return m_Value / 16f; }
            set { m_Value = (sbyte)(value * 16); }
        }

        public Fract8Bit()
            : this(0f) { }

        public Fract8Bit(float value) {
            AsFloat = value;
        }

        public Fract8Bit(sbyte value) {
            m_Value = value;
        }

        public static Fract8Bit FromStream(Stream stream) {
            return new Fract8Bit(BinaryFileHelper.ReadSByte(stream));
        }
    }
}