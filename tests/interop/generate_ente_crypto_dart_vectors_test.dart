// Run from ente_crypto_dart revision 981a9e0f4a227023991af3332ef0e6ab6d14a1c2
// with Ente's CI-pinned Flutter SDK. This file intentionally lives outside a
// Dart package so routine .NET builds do not download Flutter dependencies.
import 'dart:convert';
import 'dart:ffi';
import 'dart:io';
import 'dart:typed_data';
import 'package:ente_crypto_dart/ente_crypto_dart.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:sodium/sodium_sumo.dart';

Future<void> initializeSodium() async {
  sodium = await SodiumSumoInit.init(() => DynamicLibrary.open('libsodium.so.23'));
}

void main() {
  test('emit Dart to .NET fixture', () async {
    await initializeSodium();
    final entityKey = Uint8List.fromList(List<int>.generate(32, (i) => i));
    final masterKey = Uint8List.fromList(List<int>.generate(32, (i) => 255 - i));
    final authKey = Uint8List.fromList(List<int>.generate(32, (i) => (i * 7) & 255));
    final plaintext = Uint8List.fromList(utf8.encode(
      'otpauth://totp/Ente%20Interop:person%40example.test?secret=JBSWY3DPEHPK3PXP&issuer=Ente%20Interop&algorithm=SHA1&digits=6&period=30'));
    final entity = await CryptoUtil.encryptData(plaintext, entityKey);
    final wrapped = CryptoUtil.encryptSync(authKey, masterKey);
    print('ENTE_VECTOR=' + jsonEncode({
      'producer': 'ente_crypto_dart',
      'revision': '981a9e0f4a227023991af3332ef0e6ab6d14a1c2',
      'entityKey': base64Encode(entityKey),
      'entityPlaintext': base64Encode(plaintext),
      'entityHeader': base64Encode(entity.header!),
      'entityCiphertext': base64Encode(entity.encryptedData!),
      'masterKey': base64Encode(masterKey),
      'authenticatorKey': base64Encode(authKey),
      'wrappedKeyNonce': base64Encode(wrapped.nonce!),
      'wrappedKeyCiphertext': base64Encode(wrapped.encryptedData!),
    }));
  });

  test('decrypt .NET fixture', () async {
    await initializeSodium();
    final vectorPath = Platform.environment['NET_VECTOR_PATH'];
    if (vectorPath == null) return;
    final vector = jsonDecode(await File(vectorPath).readAsString()) as Map<String, dynamic>;
    final plaintext = await CryptoUtil.decryptData(
      base64Decode(vector['entityCiphertext']),
      base64Decode(vector['entityKey']),
      base64Decode(vector['entityHeader']),
    );
    expect(base64Encode(plaintext), vector['entityPlaintext']);
    final authKey = CryptoUtil.decryptSync(
      base64Decode(vector['wrappedKeyCiphertext']),
      base64Decode(vector['masterKey']),
      base64Decode(vector['wrappedKeyNonce']),
    );
    expect(base64Encode(authKey), vector['authenticatorKey']);
  });

  test('emit Ente Auth encrypted-backup fixture', () async {
    await initializeSodium();
    final password = Uint8List.fromList(utf8.encode('correct horse battery staple'));
    final salt = Uint8List.fromList(List<int>.generate(16, (i) => 160 + i));
    final key = await CryptoUtil.deriveKey(password, salt, 8388608, 1);
    final plaintext = Uint8List.fromList(utf8.encode(
      'otpauth://totp/Backup:test?secret=JBSWY3DPEHPK3PXP&issuer=Backup'));
    final encrypted = await CryptoUtil.encryptData(plaintext, key);
    print('ENTE_BACKUP_VECTOR=' + jsonEncode({
      'version': 1,
      'kdfParams': {'memLimit': 8388608, 'opsLimit': 1, 'salt': base64Encode(salt)},
      'encryptedData': base64Encode(encrypted.encryptedData!),
      'encryptionNonce': base64Encode(encrypted.header!),
    }));
  });

  test('decrypt .NET encrypted backup', () async {
    await initializeSodium();
    final backupPath = Platform.environment['NET_BACKUP_PATH'];
    if (backupPath == null) return;
    final document = jsonDecode(await File(backupPath).readAsString()) as Map<String, dynamic>;
    final kdf = document['kdfParams'] as Map<String, dynamic>;
    final key = await CryptoUtil.deriveKey(
      Uint8List.fromList(utf8.encode('correct horse battery staple')),
      base64Decode(kdf['salt']),
      kdf['memLimit'],
      kdf['opsLimit'],
    );
    final plaintext = await CryptoUtil.decryptData(
      base64Decode(document['encryptedData']),
      key,
      base64Decode(document['encryptionNonce']),
    );
    expect(utf8.decode(plaintext),
      'otpauth://totp/Backup:test?secret=JBSWY3DPEHPK3PXP&issuer=Backup');
  });

  test('emit login KDF and sealed-token fixture', () async {
    await initializeSodium();
    final keyEncryptionKey = Uint8List.fromList(List<int>.generate(32, (i) => 31 - i));
    final loginKey = await CryptoUtil.deriveLoginKey(keyEncryptionKey);
    final keyPair = CryptoUtil.generateKeyPair();
    final token = Uint8List.fromList(utf8.encode('interop-session-token'));
    final sealed = CryptoUtil.sealSync(token, keyPair.publicKey);
    print('ENTE_LOGIN_VECTOR=' + jsonEncode({
      'keyEncryptionKey': base64Encode(keyEncryptionKey),
      'loginKey': base64Encode(loginKey),
      'publicKey': base64Encode(keyPair.publicKey),
      'secretKey': base64Encode(keyPair.secretKey.extractBytes()),
      'sealedToken': base64Encode(sealed),
      'token': base64Encode(token),
    }));
  });
}
