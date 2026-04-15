# Encryption and Cryptography Basics

## What Is Encryption

Encryption transforms readable data (plaintext) into an unreadable form (ciphertext) using a mathematical algorithm and a key. Only someone with the correct key can reverse the process (decryption) and recover the original data. Encryption protects data confidentiality — even if an attacker intercepts the ciphertext, they cannot read it without the key.

## Symmetric Encryption

In symmetric encryption, the same key is used for both encryption and decryption. It is fast and efficient for large amounts of data.

### AES (Advanced Encryption Standard)

AES is the most widely used symmetric cipher. It operates on fixed-size 128-bit blocks and supports key sizes of 128, 192, or 256 bits. AES-256 is the standard for classified government data and is considered secure against all known attacks, including quantum computing (Grover's algorithm only halves the effective key length, making AES-256 equivalent to AES-128 against a quantum adversary).

### Modes of Operation

A block cipher like AES encrypts one block at a time. The mode of operation determines how blocks are chained together:

- **ECB (Electronic Codebook)**: Each block is encrypted independently. Identical plaintext blocks produce identical ciphertext blocks — this leaks patterns. Never use ECB for anything beyond single-block encryption.
- **CBC (Cipher Block Chaining)**: Each block is XORed with the previous ciphertext block before encryption. Requires an initialization vector (IV). Widely used but vulnerable to padding oracle attacks if not implemented carefully.
- **GCM (Galois/Counter Mode)**: Combines encryption with authentication — it produces both ciphertext and an authentication tag that detects tampering. AES-GCM is the recommended mode for most applications. It is the cipher suite used in TLS 1.3.

## Asymmetric Encryption (Public-Key Cryptography)

Asymmetric encryption uses a key pair: a public key (shared openly) and a private key (kept secret). Data encrypted with the public key can only be decrypted with the corresponding private key, and vice versa.

### RSA

RSA is based on the mathematical difficulty of factoring the product of two large primes. Key sizes of 2048 bits are the minimum; 4096 bits are recommended for long-term security. RSA is slower than symmetric encryption by orders of magnitude and is typically used only to encrypt a symmetric session key, which then encrypts the bulk data (hybrid encryption).

### Elliptic Curve Cryptography (ECC)

ECC achieves equivalent security to RSA with much smaller keys. A 256-bit ECC key provides roughly the same security as a 3072-bit RSA key. ECC is used in modern TLS, SSH (Ed25519 keys), and cryptocurrency (secp256k1 for Bitcoin, Ed25519 for others). Curve25519 and Ed25519 (designed by Daniel J. Bernstein) are widely recommended for their speed, simplicity, and resistance to implementation errors.

## Hashing

A hash function maps input of arbitrary length to a fixed-length output (digest). Cryptographic hash functions must be:
- **Pre-image resistant**: Given a hash output, it's infeasible to find the input
- **Collision resistant**: It's infeasible to find two different inputs with the same hash
- **Avalanche effect**: A single-bit change in input produces a completely different hash

Common hash functions:
- **SHA-256**: 256-bit output. Used in TLS certificates, Bitcoin, and data integrity verification.
- **SHA-3**: Alternative to SHA-2, based on the Keccak sponge construction. Used when algorithm diversity is desired.
- **BLAKE3**: Extremely fast, tree-hashable, 256-bit output. Increasingly used in content-addressable storage and integrity checking.

### Password Hashing

General-purpose hash functions (SHA-256) are too fast for password storage — an attacker can try billions of guesses per second. Use deliberately slow password hashing algorithms:
- **Argon2id**: Winner of the Password Hashing Competition. Configurable memory, time, and parallelism costs. Recommended as the default choice.
- **bcrypt**: Widely used, well-understood, with a built-in salt and configurable work factor. Maximum input length of 72 bytes.
- **scrypt**: Memory-hard, making GPU-based attacks expensive. Used in some cryptocurrency proof-of-work systems.

## Digital Signatures

A digital signature proves that a message was created by the holder of a specific private key and has not been altered. The signer hashes the message and encrypts the hash with their private key. The recipient decrypts the hash with the signer's public key and compares it to their own hash of the message. If they match, the signature is valid.

Ed25519 signatures are fast (creating or verifying a signature takes microseconds), compact (64 bytes), and deterministic (no random nonce needed, eliminating a class of implementation vulnerabilities).

## TLS (Transport Layer Security)

TLS encrypts data in transit between a client and server. The handshake establishes a shared session key using asymmetric cryptography, then bulk data transfer uses symmetric encryption (typically AES-GCM).

TLS 1.3 (the current version) simplified the handshake to a single round trip, removed support for insecure algorithms (RSA key exchange, CBC mode, SHA-1), and mandates forward secrecy — even if the server's private key is compromised later, past sessions cannot be decrypted.

## Key Management

Encryption is only as strong as its key management. Keys stored in plaintext on disk are effectively unencrypted. Best practices:
- Store keys in hardware security modules (HSMs) or cloud KMS services
- Rotate encryption keys periodically
- Use envelope encryption: encrypt data with a data encryption key (DEK), then encrypt the DEK with a key encryption key (KEK) stored in the KMS
- Never commit keys, certificates, or secrets to version control
