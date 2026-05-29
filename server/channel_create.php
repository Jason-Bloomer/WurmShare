<?php
/**
 * channel_create.php
 * ============================================================================
 * Endpoint: POST /channel_create.php
 *
 * Creates a new password-protected channel.
 *
 * Request body (JSON):
 *   {
 *     "channel_id": "<64-char hex>",   // Hex( SHA-256(password) )
 *     "auth_token":  "<64-char hex>"   // Hex( SHA-256(channel_id_bytes) )
 *   }
 *
 * Responses:
 *   201  { "status": "created" }
 *   409  { "error": "Channel already exists — use join instead" }
 *   401  { "error": "Credential verification failed" }
 *   400  { "error": "..." }   missing / malformed fields
 *   405  wrong HTTP method
 * ============================================================================
 */

declare(strict_types=1);
require_once __DIR__ . '/_common.php';

requireMethod('POST');

try {
    $body      = readJsonBody();
    $channelId = requireHex64($body, 'channel_id');
    $authToken = requireHex64($body, 'auth_token');

    // Verify SHA-256( hex2bin(channel_id) ) === auth_token
    // This proves internal consistency without the server ever seeing the password.
    if (!verifyAuthToken($channelId, $authToken)) {
        jsonResponse(['error' => 'Credential verification failed'], 401);
    }

    $db = getDB();

    // INSERT IGNORE silently skips on duplicate key; rowCount() tells us which happened.
    $stmt = $db->prepare(
        'INSERT IGNORE INTO channels (channel_id, auth_token, created_at)
         VALUES (:cid, :tok, UTC_TIMESTAMP())'
    );
    $stmt->execute([':cid' => $channelId, ':tok' => $authToken]);

    if ($stmt->rowCount() === 0) {
        jsonResponse(['error' => 'Channel already exists — use join instead'], 409);
    }

    jsonResponse(['status' => 'created'], 201);

} catch (PDOException $e) {
    error_log('[SecureChannel] channel_create.php DB error: ' . $e->getMessage());
    jsonResponse(['error' => 'Internal server error'], 500);
} catch (Throwable $e) {
    error_log('[SecureChannel] channel_create.php error: ' . $e->getMessage());
    jsonResponse(['error' => 'Internal server error'], 500);
}
