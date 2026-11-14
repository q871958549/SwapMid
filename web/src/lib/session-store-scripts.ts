export const CONDITIONAL_SESSION_MUTATION_SCRIPT = `
local raw = redis.call('GET', KEYS[1])
if not raw then return 0 end

local ok, current = pcall(cjson.decode, raw)
if not ok or type(current) ~= 'table' then return -1 end

local currentVersion = current['identityVersion']
if currentVersion == nil then currentVersion = '' end
if currentVersion ~= ARGV[1]
  or current['userName'] ~= ARGV[2]
  or current['accessToken'] ~= ARGV[3]
  or (current['refreshToken'] or '') ~= ARGV[4]
  or current['accessTokenExpiresAt'] ~= tonumber(ARGV[5]) then
  return -1
end

if ARGV[6] == 'delete' then
  redis.call('DEL', KEYS[1], KEYS[2], KEYS[3])
  return 1
end

local updated = redis.call('SET', KEYS[1], ARGV[7], 'KEEPTTL', 'XX')
if not updated then return 0 end
redis.call('DEL', KEYS[2])
return 1
`

export const CONDITIONAL_SESSION_INVALIDATION_SCRIPT = `
local raw = redis.call('GET', KEYS[1])
if not raw then return 0 end

local ok, current = pcall(cjson.decode, raw)
if not ok or type(current) ~= 'table' then return -1 end

local currentVersion = current['identityVersion']
if currentVersion == nil then currentVersion = '' end
if currentVersion ~= ARGV[1]
  or current['userName'] ~= ARGV[2]
  or current['accessToken'] ~= ARGV[3]
  or (current['refreshToken'] or '') ~= ARGV[4]
  or current['accessTokenExpiresAt'] ~= tonumber(ARGV[5]) then
  return -1
end

local leaseRaw = redis.call('GET', KEYS[2])
if leaseRaw then
  local leaseOk, lease = pcall(cjson.decode, leaseRaw)
  if leaseOk and type(lease) == 'table'
    and lease['source'] == ARGV[6]
    and (lease['state'] == 'active' or lease['state'] == 'cooldown')
    and redis.call('PTTL', KEYS[2]) > 0 then
      return 2
  end
  redis.call('DEL', KEYS[2])
end

redis.call('DEL', KEYS[1], KEYS[2], KEYS[3])
return 1
`

export const IDENTITY_SAFE_SESSION_MUTATION_SCRIPT = `
local raw = redis.call('GET', KEYS[1])
if not raw then return 0 end

local ok, current = pcall(cjson.decode, raw)
if not ok or type(current) ~= 'table' then return -1 end

local currentVersion = current['identityVersion']
if currentVersion == nil then currentVersion = '' end
if currentVersion ~= ARGV[1]
  or current['userName'] ~= ARGV[2]
  or current['accessToken'] ~= ARGV[3]
  or (current['refreshToken'] or '') ~= ARGV[4]
  or current['accessTokenExpiresAt'] ~= tonumber(ARGV[5]) then
  return -1
end

local leaseRaw = redis.call('GET', KEYS[2])
if leaseRaw then
  local leaseOk, lease = pcall(cjson.decode, leaseRaw)
  if leaseOk and type(lease) == 'table'
    and lease['source'] == ARGV[6]
    and (lease['state'] == 'active' or lease['state'] == 'cooldown')
    and redis.call('PTTL', KEYS[2]) > 0 then
      return 2
  end
  redis.call('DEL', KEYS[2])
end

local updated = redis.call('SET', KEYS[1], ARGV[7], 'KEEPTTL', 'XX')
if not updated then return 0 end
redis.call('DEL', KEYS[2])
return 1
`
