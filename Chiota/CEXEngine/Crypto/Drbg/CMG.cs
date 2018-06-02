﻿#region Directives
using System;
using System.Threading.Tasks;
using VTDev.Libraries.CEXEngine.Crypto.Cipher.Symmetric.Block;
using VTDev.Libraries.CEXEngine.Crypto.Common;
using VTDev.Libraries.CEXEngine.Crypto.Drbg;
using VTDev.Libraries.CEXEngine.Crypto.Enumeration;
using VTDev.Libraries.CEXEngine.CryptoException;
#endregion

#region License Information
// The MIT License (MIT)
// 
// Copyright (c) 2016 vtdev.com
// This file is part of the CEX Cryptographic library.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
// Implementation Details:
// An implementation of a Counter based Deterministic Random Byte Generator (CTRDRBG). 
// Written by John Underhill, November 21, 2014
// Updated October 10, 2016
// Contact: develop@vtdev.com
#endregion

namespace VTDev.Libraries.CEXEngine.Crypto.Generator
{
    /// <summary>
    /// An implementation of a block cipher Counter Mode Generator (CMG)
    /// </summary> 
    /// 
    /// <example>
    /// <description>Generate an array of pseudo random bytes:</description>
    /// <code>
    /// using (IDrbg rnd = new CTRDrbg(BlockCiphers.RHX))
    /// {
    ///     // initialize
    ///     rnd.Initialize(Seed, [Nonce], [Info]);
    ///     // generate bytes
    ///     rnd.Generate(Output, [Offset], [Size]);
    /// }
    /// </code>
    /// </example>
    /// 
    /// <remarks>
    /// <description><B>Overview:</B></description>
    /// <para>The Counter mode generates a key-stream by encrypting successive values of an incrementing Big Endian ordered 8bit integer counter array.<BR></BR>
    /// The key-stream is then XOR'd with the input message block creating a type of stream cipher.<BR></BR>
    /// In parallel mode, the generators counter is increased by a number factored from the number of input blocks, allowing for a multi-threaded operation.</para>
    /// 
    /// <description><B>Description:</B></description>
    /// <para><EM>Legend:</EM><BR></BR> 
    /// <B>C</B>=pseudo-random, <B>K</B>=seed, <B>E</B>=encrypt<BR></BR>
    /// <EM>Generate</EM><BR></BR>
    /// R0 ← IV. For 1 ≤ j ≤ t, Cj ← EK(Cj), C+1.</para><BR></BR>
    ///
    /// <description><B>Multi-Threading:</B></description>
    /// <para>The transformation function in a CTR generator is not limited by a dependency chain; this mode can be both SIMD pipelined and multi-threaded.
    /// Output from the parallelized functions aligns with the output from a standard sequential CTR implementation processing an all zeroes input array.<BR></BR>
    /// Parallelism is acheived by pre-calculating the counters positional offset over multiple 'chunks' of key-stream, which are then generated independently across threads.<BR></BR> 
    /// The key stream generated by encrypting the counter array, is output as a source of pseudo-random.</para>
    ///
    /// <description>Implementation Notes:</description>
    /// <list type="bullet">
    /// <item><description>The class constructor can either be initialized with a block cipher instance, or using the block ciphers enumeration name.</description></item>
    /// <item><description>A block cipher instance created using the enumeration constructor, is automatically deleted when the class is destroyed.</description></item>
    /// <item><description>The generator can be initialized with either a RngParams key container class, or with a Seed and optional inputs of Nonce and Info.</description></item>
    /// <item><description>The seed array(s) passed to the Initialize(Seed, [Nonce], [Info]) function must be cipher <c>key-size + block-size</c> in length.</description></item>
    /// <item><description>The Generate() methods can not be used until an Initialize() function has been called.</description></item>
    /// <item><description>The IsParallel property is enabled automatically if the system has more than one processor core.</description></item>
    /// <item><description>Parallel processing is enabled when the IsParallel property is set to true, and an input block of ParallelBlockSize is passed to the transform.</description></item>
    /// <item><description>The ParallelOption.MaxDegreeOfParallelism property can be used as the thread count in the parallel loop; this must be an even number no greater than the number of processer cores on the system.</description></item>
    /// <item><description>ParallelBlockSize is calculated automatically but can be user defined, but must be evenly divisible by ParallelMinimumSize.</description></item>
    /// <item><description>Parallel block calculation ex. <c>ParallelBlockSize = (data.Length / cipher.ParallelMinimumSize) * 40</c></description></item>
    /// </list>
    /// 
    /// <description>Guiding Publications:</description>
    /// <list type="number">
    /// <item><description>NIST <a href="http://csrc.nist.gov/publications/drafts/800-90/draft-sp800-90b.pdf">SP800-90B</a>: Recommendation for the Entropy Sources Used for Random Bit Generation.</description></item>
    /// <item><description>NIST <a href="http://csrc.nist.gov/publications/fips/fips140-2/fips1402.pdf">Fips 140-2</a>: Security Requirments For Cryptographic Modules.</description></item>
    /// <item><description>NIST <a href="http://csrc.nist.gov/groups/ST/toolkit/rng/documents/SP800-22rev1a.pdf">SP800-22 1a</a>: A Statistical Test Suite for Random and Pseudorandom Number Generators for Cryptographic Applications.</description></item>
    /// <item><description>NIST <a href="http://eprint.iacr.org/2006/379.pdf">Security Bounds</a> for the Codebook-based: Deterministic Random Bit Generator.</description></item>
    /// </list>
    /// </remarks>
    public sealed class CMG : IDrbg
    {
        #region Constants
        private const string ALG_NAME = "CTRDrbg";
        private const int BLOCK_SIZE = 16;
        private const int CTR_SIZE = 16;
        private const int MAX_PARALLEL = 1024000;
        private const int MIN_PARALLEL = 1024;
        private const int PRL_BLOCKCACHE = 32000;
        #endregion

        #region Fields
        private int m_blockSize = BLOCK_SIZE;
        private IBlockCipher m_blockCipher;
        private byte[] m_ctrVector;
        private bool m_disposeEngine = true;
        private bool m_isDisposed = false;
        private bool m_isInitialized = false;
        private bool m_isParallel = false;
        private int[] m_legalKeySizes;
        private int m_minKeySize = 32 + CTR_SIZE;
        private int m_parallelBlockSize = PRL_BLOCKCACHE;
        private int m_parallelMinimumSize = 0;
        private ParallelOptions m_parallelOption = null;
        private int m_processorCount = 1;
        #endregion

        #region Properties
        /// <summary>
        /// Get: Generator is ready to produce random
        /// </summary>
        public bool IsInitialized
        {
            get { return m_isInitialized; }
            private set { m_isInitialized = value; }
        }

        /// <summary>
        /// Get/Set Automatic processor parallelization
        /// </summary>
        public bool IsParallel
        {
            get { return m_isParallel; }
            set { m_isParallel = value; }
        }

        /// <summary>
        /// Minimum initialization key size in bytes.
        /// <para>Combined sizes of key, nonce, and info must be at least this size.</para>
        /// </summary>
        public int MinSeedSize
        {
            get { return m_minKeySize; }
            private set { m_minKeySize = value; }
        }

        /// <summary>
        /// Get: The generators type name
        /// </summary>
        public Drbgs Enumeral 
        {
            get { return Drbgs.CMG; }
        }

        /// <summary>
        /// Get: Available Encryption Key Sizes in bytes
        /// </summary>
        public int[] LegalKeySizes
        {
            get { return m_legalKeySizes; }
        }

        /// <summary>
        /// Get: Algorithm Name
        /// </summary>
        public string Name
        {
            get { return ALG_NAME; }
        }

        /// <summary>
        /// Get/Set: Parallel block size. Must be a multiple of <see cref="ParallelMinimumSize"/>.
        /// </summary>
        /// 
        /// <exception cref="CryptoSymmetricException">Thrown if a parallel block size is not evenly divisible by ParallelMinimumSize, or  block size is less than ParallelMinimumSize or more than ParallelMaximumSize values</exception>
        public int ParallelBlockSize
        {
            get { return m_parallelBlockSize; }
            set
            {
                if (value % ParallelMinimumSize != 0)
                    throw new CryptoSymmetricException("ChaCha:ParallelBlockSize", String.Format("Parallel block size must be evenly divisible by ParallelMinimumSize: {0}", ParallelMinimumSize), new ArgumentException());
                if (value > ParallelMaximumSize || value < ParallelMinimumSize)
                    throw new CryptoSymmetricException("ChaCha:ParallelBlockSize", String.Format("Parallel block must be Maximum of ParallelMaximumSize: {0} and evenly divisible by ParallelMinimumSize: {1}", ParallelMaximumSize, ParallelMinimumSize), new ArgumentOutOfRangeException());

                m_parallelBlockSize = value;
            }
        }

        /// <summary>
        /// Get: Maximum input size with parallel processing
        /// </summary>
        public static int ParallelMaximumSize
        {
            get { return MAX_PARALLEL; }
        }

        /// <summary>
        /// Get: Minimum input size to trigger parallel processing
        /// </summary>
        public static int ParallelMinimumSize
        {
            get { return MIN_PARALLEL; }
        }

        /// <summary>
        /// Get/Set: The parallel loops ParallelOptions
        /// <para>The MaxDegreeOfParallelism of the parallel loop is equal to the Environment.ProcessorCount by default</para>
        /// </summary>
        public ParallelOptions ParallelOption
        {
            get
            {
                if (m_parallelOption == null)
                    m_parallelOption = new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount };

                return m_parallelOption;
            }
            set
            {
                if (value != null)
                {
                    if (value.MaxDegreeOfParallelism < 1)
                        throw new CryptoSymmetricException("CMG:ParallelOption", "MaxDegreeOfParallelism can not be less than 1!", new ArgumentException());
                    else if (value.MaxDegreeOfParallelism == 1)
                        m_isParallel = false;
                    else if (value.MaxDegreeOfParallelism % 2 != 0)
                        throw new CryptoSymmetricException("CMG:ParallelOption", "MaxDegreeOfParallelism can not be an odd number; must be either 1, or a divisible of 2!", new ArgumentException());

                    m_parallelOption = value;
                }
            }
        }

        /// <remarks>
        /// Get: Processor count
        /// </remarks>
        private int ProcessorCount
        {
            get { return m_processorCount; }
            set { m_processorCount = value; }
        }

        #endregion

        #region Constructor
        /// <summary>
        /// Creates a CTR Bytes Generator using a block cipher type name
        /// </summary>
        /// 
        /// <param name="CipherType">The formal enumeration name of a block cipher</param>
        /// 
        /// <exception cref="CryptoSymmetricException">Thrown if an invalid Cipher type is used</exception>
        public CMG(BlockCiphers CipherType)
        {
            if (CipherType == BlockCiphers.None)
                throw new CryptoSymmetricException("CMG:CTor", "The Cipher type can not be none!", new ArgumentNullException());

            m_disposeEngine = true;
            m_blockCipher = LoadCipher(CipherType);
            m_blockSize = m_blockCipher.BlockSize;
            m_legalKeySizes = new int[m_blockCipher.LegalKeySizes.Length];

            for (int i = 0; i < m_blockCipher.LegalKeySizes.Length; ++i)
                m_legalKeySizes[i] = m_blockCipher.LegalKeySizes[i] + m_blockCipher.BlockSize;

            Scope();
        }

        /// <summary>
        /// Creates a CTR Bytes Generator using a block cipher instance
        /// </summary>
        /// 
        /// <param name="Cipher">The block cipher instance</param>
        /// <param name="DisposeEngine">Dispose of the block cipher instance when <see cref="Dispose()"/> on this class is called</param>
        /// 
        /// <exception cref="CryptoSymmetricException">Thrown if a null block cipher is used</exception>
        public CMG(IBlockCipher Cipher, bool DisposeEngine = true)
        {
            if (Cipher == null)
                throw new CryptoGeneratorException("CTRDrbg:Ctor", "Cipher can not be null!", new ArgumentNullException());

            m_disposeEngine = DisposeEngine;
            m_blockCipher = Cipher;
            m_blockSize = m_blockCipher.BlockSize;
            m_legalKeySizes = new int[m_blockCipher.LegalKeySizes.Length];

            for (int i = 0; i < m_blockCipher.LegalKeySizes.Length; ++i)
                m_legalKeySizes[i] = m_blockCipher.LegalKeySizes[i] + m_blockCipher.BlockSize;

            Scope();
        }

        private CMG()
        {
        }

        /// <summary>
        /// Finalize objects
        /// </summary>
        ~CMG()
        {
            Dispose(false);
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Generate a block of pseudo random bytes
        /// </summary>
        /// 
        /// <param name="Output">Output array filled with random bytes</param>
        /// 
        /// <returns>Number of bytes generated</returns>
        public int Generate(byte[] Output)
        {
            ParallelTransform(Output, 0);

            return Output.Length;
        }

        /// <summary>
        /// Generate pseudo random bytes using offset and length parameters
        /// </summary>
        /// 
        /// <param name="Output">Output array filled with random bytes</param>
        /// <param name="OutOffset">Position within Output array</param>
        /// <param name="Size">Number of bytes to generate</param>
        /// 
        /// <returns>Number of bytes generated</returns>
        /// 
        /// <exception cref="CryptoGeneratorException">Thrown if the output buffer is too small</exception>
        public int Generate(byte[] Output, int OutOffset, int Size)
        {
            if ((Output.Length - Size) < OutOffset)
                throw new CryptoGeneratorException("CTRDrbg:Generate", "Output buffer too small!", new Exception());

            ParallelTransform(Output, OutOffset);

            return Size;
        }

        /// <summary>
        /// Initialize the generator with a RngParams structure containing the key, and optional nonce, and info string.
        /// <para>The combined length of the keying material must be a legal key size; 24 or 40 bytes.</para>
        /// </summary>
        /// 
        /// <param name="GenParam">The RngParams containing the generators keying material</param>
        public void Initialize(RngParams GenParam)
        {
            if (GenParam.Nonce.Length != 0)
            {
                if (GenParam.Info.Length != 0)

                    Initialize(GenParam.Seed, GenParam.Nonce, GenParam.Info);
                else

                    Initialize(GenParam.Seed, GenParam.Nonce);
            }
            else
            {

                Initialize(GenParam.Seed);
            }
        }

        /// <summary>
        /// Initialize the generator with a key.
        /// <para>The key length must be a legal key size; 24 or 40 bytes.</para>
        /// </summary>
        /// 
        /// <param name="Seed">The primary key array used to seed the generator</param>
        /// 
        /// <exception cref="CryptoGeneratorException">Thrown if an invalid or null key is used</exception>
        public void Initialize(byte[] Seed)
        {
            // recheck params
            Scope();

            if (Seed == null)
                throw new CryptoGeneratorException("CTRDrbg:Initialize", "nonce can not be null!", new ArgumentNullException());
            if (!IsValidSeedSize(Seed.Length))
                throw new CryptoGeneratorException("CTRDrbg:Initialize", string.Format("Minimum key size has not been added. Size must be at least {0} bytes!", m_minKeySize + CTR_SIZE), new ArgumentOutOfRangeException());

            m_ctrVector = new byte[m_blockSize];
            Buffer.BlockCopy(Seed, 0, m_ctrVector, 0, m_blockSize);
            int keyLen = Seed.Length - m_blockSize;
            byte[] key = new byte[keyLen];
            Buffer.BlockCopy(Seed, m_blockSize, key, 0, keyLen);

            m_blockCipher.Initialize(true, new KeyParams(key));
            m_isInitialized = true;
        }

        /// <summary>
        /// Initialize the generator with key and nonce arrays.
        /// <para>The combined length of the keying material must be a legal key size; 24 or 40 bytes.</para>
        /// </summary>
        /// 
        /// <param name="Seed">The primary key array used to seed the generator</param>
        /// <param name="Nonce">The nonce value containing an additional source of entropy</param>
        /// 
        /// <exception cref="CryptoGeneratorException">Thrown if an invalid or null seed or nonce is used</exception>
        public void Initialize(byte[] Seed, byte[] Nonce)
        {
            byte[] seed = new byte[Seed.Length + Nonce.Length];

            Buffer.BlockCopy(Seed, 0, seed, 0, Seed.Length);
            Buffer.BlockCopy(Nonce, 0, seed, Seed.Length, Nonce.Length);

            Initialize(seed);
        }

        /// <summary>
        /// Initialize the generator with a key, a nonce array, and an information string or nonce
        /// </summary>
        /// 
        /// <param name="Seed">The primary key array used to seed the generator</param>
        /// <param name="Nonce">The nonce value used as an additional source of entropy</param>
        /// <param name="Info">The information string or nonce used as a third source of entropy</param>
        /// 
        /// <exception cref="CryptoGeneratorException">Thrown if a null seed, nonce, or info string is used</exception>
        public void Initialize(byte[] Seed, byte[] Nonce, byte[] Info)
        {
            byte[] seed = new byte[Seed.Length + Nonce.Length + Info.Length];

            Buffer.BlockCopy(Seed, 0, seed, 0, Seed.Length);
            Buffer.BlockCopy(Nonce, 0, seed, Seed.Length, Nonce.Length);
            Buffer.BlockCopy(Info, 0, seed, Nonce.Length + Seed.Length, Info.Length);

            Initialize(seed);
        }

        /// <summary>
        /// Update the generators seed value.
        /// <para>If the seed array size is equal to a legal key size, the key and counter are replaced with the new values.
        /// If the seed array size is equal to the counter value (16 or 32 bytes), the counter is replaced.</para>
        /// </summary>
        /// 
        /// <param name="Seed">The seed value array</param>
        /// 
        /// <exception cref="CryptoGeneratorException">Thrown if a null Seed is used</exception>
        public void Update(byte[] Seed)
        {
            if (Seed == null)
                throw new CryptoGeneratorException("CTRDrbg:Update", "Seed can not be null!", new ArgumentNullException());

            if (Seed.Length >= MinSeedSize)
                Initialize(Seed);
            else if (Seed.Length >= m_blockSize)
                Buffer.BlockCopy(Seed, 0, m_ctrVector, 0, m_ctrVector.Length);
        }
        #endregion

        #region Private Methods
        private void Generate(int Size, byte[] Counter, byte[] Output, int OutOffset)
        {
            int aln = Size - (Size % m_blockSize);
            int ctr = 0;

            while (ctr != aln)
            {
                m_blockCipher.EncryptBlock(Counter, 0, Output, OutOffset + ctr);
                Increment(Counter);
                ctr += m_blockSize;
            }

            if (ctr != Size)
            {
                byte[] outputBlock = new byte[m_blockSize];
                m_blockCipher.EncryptBlock(Counter, outputBlock);
                int fnlSize = Size % m_blockSize;
                Buffer.BlockCopy(outputBlock, 0, Output, OutOffset + (Size - fnlSize), fnlSize);
                Increment(Counter);
            }
        }

        private void ParallelTransform(byte[] Output, int OutOffset)
        {
            int blklen = Output.Length - OutOffset;

            if (!m_isParallel || blklen < MIN_PARALLEL)
	        {
		        // generate random
                Generate(blklen, m_ctrVector, Output, OutOffset);
	        }
	        else
	        {
		        // parallel CTR processing //
                int cnksize = (blklen / m_blockSize / ProcessorCount) * m_blockSize;
		        int rndsize = cnksize * ProcessorCount;
		        int subsize = (cnksize / m_blockSize);
                byte[] tmpCtr = new byte[m_ctrVector.Length];

                // create random, and xor to output in parallel
                System.Threading.Tasks.Parallel.For(0, ProcessorCount, i =>
                {
                    // thread level counter
                    byte[] thdCtr = new byte[m_ctrVector.Length];
                    // offset counter by chunk size / block size
                    thdCtr = Increase(m_ctrVector, subsize * i);
                    // create random at offset position
                    Generate(cnksize, thdCtr, Output, (i * cnksize));
                    if (i == m_processorCount - 1)
                        Array.Copy(thdCtr, 0, tmpCtr, 0, thdCtr.Length);
                });

		        // last block processing
                if (rndsize < blklen)
		        {
                    int fnlsize = blklen % rndsize;
                    Generate(fnlsize, tmpCtr, Output, rndsize);
		        }

                // copy the last counter position to class variable
                Buffer.BlockCopy(tmpCtr, 0, m_ctrVector, 0, m_ctrVector.Length);
	        }
        }

        private static void Increment(byte[] Counter)
        {
            int i = Counter.Length;
            while (--i >= 0 && ++Counter[i] == 0) { }
        }

        private static byte[] Increase(byte[] Counter, int Size)
        {
            int carry = 0;
            byte[] buffer = new byte[Counter.Length];
            int offset = buffer.Length - 1;
            byte[] cnt = BitConverter.GetBytes(Size);
            byte osrc, odst, ndst;
            Buffer.BlockCopy(Counter, 0, buffer, 0, Counter.Length);

            for (int i = offset; i > 0; i--)
            {
                odst = buffer[i];
                osrc = offset - i < cnt.Length ? cnt[offset - i] : (byte)0;
                ndst = (byte)(odst + osrc + carry);
                carry = ndst < odst ? 1 : 0;
                buffer[i] = ndst;
            }

            return buffer;
        }

        private bool IsValidSeedSize(int SeedSize)
	    {
		    for (int i = 0; i < m_blockCipher.LegalKeySizes.Length; ++i)
		    {
			    if (SeedSize == m_legalKeySizes[i])
				    break;
			    if (i == m_legalKeySizes.Length - 1)
				    return false;
		    }
		    return true;
	    }

        IBlockCipher LoadCipher(BlockCiphers CipherType)
        {
            try
            {
                return Helper.BlockCipherFromName.GetInstance(CipherType);
            }
            catch (Exception ex)
            {
                throw new CryptoSymmetricException("CMG:LoadCipher", "The block cipher could not be instantiated!", ex);
            }
        }

        void Scope()
        {
            m_processorCount = Environment.ProcessorCount;
            if (ProcessorCount % 2 != 0)
                ProcessorCount--;

            if (m_processorCount > 1)
            {
                if (m_parallelOption != null && m_parallelOption.MaxDegreeOfParallelism > 0 && (m_parallelOption.MaxDegreeOfParallelism % 2 == 0))
                    m_processorCount = m_parallelOption.MaxDegreeOfParallelism;
                else
                    m_parallelOption = new ParallelOptions() { MaxDegreeOfParallelism = m_processorCount };
            }

            m_parallelMinimumSize = m_processorCount * m_blockCipher.BlockSize;
            m_parallelBlockSize = m_processorCount * PRL_BLOCKCACHE;

            if (!m_isInitialized)
                m_isParallel = (m_processorCount > 1);
        }

        #endregion

        #region IDispose
        /// <summary>
        /// Dispose of this class, and dependant resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool Disposing)
        {
            if (!m_isDisposed && Disposing)
            {
                try
                {
                    if (m_blockCipher != null && m_disposeEngine)
                    {
                        m_blockCipher.Dispose();
                        m_blockCipher = null;
                    }
                    if (m_ctrVector != null)
                    {
                        Array.Clear(m_ctrVector, 0, m_ctrVector.Length);
                        m_ctrVector = null;
                    }
                }
                finally
                {
                    m_isDisposed = true;
                }
            }
        }
        #endregion
    }
}