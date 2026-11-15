using System;

namespace NoiseRun
{
    /// <summary>
    /// radix-2 반복형 Cooley–Tukey FFT(구현명세 5.3). 표는 생성자에서 한 번만 만든다.
    /// 인스턴스는 상태가 없으므로(표만 읽음) 여러 스레드에서 같이 써도 된다.
    /// </summary>
    public sealed class Fft
    {
        readonly int _n;
        readonly int[] _bitReverse;
        readonly float[] _cos;   // cos(2πk/n), k = 0..n/2-1
        readonly float[] _sin;   // sin(2πk/n)

        public Fft(int n)
        {
            if (n < 2 || (n & (n - 1)) != 0)
                throw new ArgumentException("FFT 크기는 2 이상의 2의 거듭제곱이어야 합니다.", nameof(n));
            _n = n;

            int bits = 0;
            while ((1 << bits) < n) bits++;
            _bitReverse = new int[n];
            for (int i = 0; i < n; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++)
                    if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                _bitReverse[i] = r;
            }

            int half = n / 2;
            _cos = new float[half];
            _sin = new float[half];
            for (int k = 0; k < half; k++)
            {
                double a = 2.0 * Math.PI * k / n;
                _cos[k] = (float)Math.Cos(a);
                _sin[k] = (float)Math.Sin(a);
            }
        }

        public int Size => _n;

        /// <summary>제자리 정방향 변환. X[k] = Σ x[j]·e^(−2πijk/n). 배열 길이는 생성자의 n과 같아야 한다.</summary>
        public void Forward(float[] re, float[] im)
        {
            if (re == null) throw new ArgumentNullException(nameof(re));
            if (im == null) throw new ArgumentNullException(nameof(im));
            if (re.Length != _n || im.Length != _n)
                throw new ArgumentException("배열 길이가 FFT 크기와 다릅니다.");

            // 비트 반전 순서로 재배치(쌍마다 한 번만 바꿈)
            for (int i = 0; i < _n; i++)
            {
                int j = _bitReverse[i];
                if (j <= i) continue;
                float tr = re[i]; re[i] = re[j]; re[j] = tr;
                float ti = im[i]; im[i] = im[j]; im[j] = ti;
            }

            // 나비 연산: 길이 len 블록마다 회전인자 e^(−2πik/len) = cos − i·sin (표 간격 n/len)
            for (int len = 2; len <= _n; len <<= 1)
            {
                int halfLen = len >> 1;
                int step = _n / len;
                for (int start = 0; start < _n; start += len)
                {
                    for (int k = 0; k < halfLen; k++)
                    {
                        float wr = _cos[k * step];
                        float wi = -_sin[k * step];
                        int a = start + k;
                        int b = a + halfLen;
                        float xr = re[b] * wr - im[b] * wi;
                        float xi = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - xr;
                        im[b] = im[a] - xi;
                        re[a] += xr;
                        im[a] += xi;
                    }
                }
            }
        }

        /// <summary>Hann 창 w[i] = 0.5 − 0.5·cos(2πi/n), i = 0..n−1.</summary>
        public static float[] Hann(int n)
        {
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
            var w = new float[n];
            for (int i = 0; i < n; i++)
                w[i] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / n));
            return w;
        }
    }
}
