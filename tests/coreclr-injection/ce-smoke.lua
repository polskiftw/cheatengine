local workspace = os.getenv('GITHUB_WORKSPACE')
if workspace == nil or workspace == '' then
  return
end

local testdir = workspace .. [[\tests\coreclr-injection]]
local resultPath = testdir .. [[\ce-result.txt]]
local markerPath = testdir .. [[\payload-ran.txt]]
local payloadPath = testdir .. [[\out\payload\CoreClrInjectionPayload.dll]]

local function writeResult(text)
  local f = assert(io.open(resultPath, 'w'))
  f:write(text)
  f:write('\n')
  f:close()
end

local timer = createTimer(nil, false)
timer.Interval = 1000
timer.OnTimer = function(t)
  t.Enabled = false

  local ok, message = pcall(function()
    hideAllCEWindows()

    if injectDotNetDLL == nil then
      dofile(getCheatEngineDir() .. [[autorun\DotNetInject.lua]])
    end

    if injectDotNetDLL == nil then
      error('DotNetInject.lua did not expose injectDotNetDLL')
    end

    local pid = getProcessIDFromProcessName('CoreClrInjectionHost.exe')
    if pid == nil or pid == 0 then
      error('CoreClrInjectionHost.exe was not found')
    end

    openProcess(pid)
    if getOpenedProcessID() ~= pid then
      error('Cheat Engine failed to open synthetic CoreCLR host')
    end

    local returnValue, injectError = injectDotNetDLL(
      payloadPath,
      'CoreClrInjectionPayload.EntryPoint',
      'Initialize',
      markerPath)

    if returnValue == nil then
      error('injectDotNetDLL failed with error ' .. tostring(injectError))
    end

    if returnValue ~= 23063 then
      error('managed payload returned ' .. tostring(returnValue) .. ', expected 23063')
    end

    local marker = io.open(markerPath, 'r')
    if marker == nil then
      error('managed payload returned successfully but marker file was not created')
    end
    marker:close()

    writeResult('SUCCESS RETURN=' .. tostring(returnValue) .. ' PID=' .. tostring(pid))
  end)

  if not ok then
    writeResult('FAIL ' .. tostring(message))
  end

  closeCE()
end

timer.Enabled = true
