/** Server codes remain open so new codes survive without being recast as a generic error. */
export type KnownApiErrorCode = 'csrf_invalid' | 'validation_error' | 'authentication_required' | 'invalid_credentials' | 'account_exists' | 'not_found' | 'idempotency_conflict' | 'stale_revision' | 'occupied' | 'wrong_turn' | 'game_finished' | 'game_in_progress' | 'invalid_move' | 'unsupported_rule_version' | 'state_integrity_error' | 'storage_unavailable' | 'rate_limit_exceeded' | 'invalid_response' | 'http_error'
export class ApiError extends Error {
  readonly status: number
  readonly code: KnownApiErrorCode | (string & {})
  readonly traceId?: string
  constructor(status: number, code: string, message: string, traceId?: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.traceId = traceId
  }
}
