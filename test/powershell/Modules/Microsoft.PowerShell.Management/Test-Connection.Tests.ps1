# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

Import-Module HelpersCommon

Describe "Test-Connection" -tags "CI" {
    BeforeAll {
        $hostName = [System.Net.Dns]::GetHostName()
        $targetName = "localhost"
        $targetAddress = "127.0.0.1"
        $targetAddressIPv6 = "::1"
        $UnreachableAddress = "10.11.12.13"

        # This resolves to an actual IPv4 address instead of just 127.0.0.1
        $realAddress = [System.Net.Dns]::GetHostEntry($hostName).AddressList |
            Where-Object AddressFamily -eq "InterNetwork" |
            Select-Object -First 1 -ExpandProperty IPAddressToString
        $jobContinues = Start-Job { Test-Connection $using:targetAddress -Repeat }
    }

    Describe "Ping Parameter Set" {

        Context 'Standard Behaviour' {
            BeforeAll {
                $pingResults = Test-Connection -Ping $targetName
                $successfulResult = $pingResults |
                    Where-Object Status -eq 'Success' |
                    Select-Object -First 1
            }

            It 'sends 4 pings by default' {
                $pingResults.Count | Should -Be 4
            }

            It 'outputs PingStatusObjects' {
                $pingResults | Should -BeOfType "Microsoft.PowerShell.Commands.TestConnectionCommand+PingStatus"
            }

            It 'numbers the ping results incrementally' {
                $pingResults.Ping | Should -Be @( 1, 2, 3, 4 )
            }

            It 'sends pings from the local machine to the targeted host' {
                $successfulResult.Source | Should -BeExactly $hostName
                $successfulResult.Destination | Should -BeExactly $targetName
                $successfulResult.Address | Should -BeExactly $targetAddressIPv6
            }

            It 'is able to ping localhost' {
                $successfulResult.Status | Should -BeExactly "Success"
            }

            It 'returns a latency value' {
                $successfulResult.Latency | Should -BeOfType "long"
            }

            It 'exposes the original PingReply and PingOptions objects from the underlying API' {
                $successfulResult.Reply | Should -BeOfType "System.Net.NetworkInformation.PingReply"
                $successfulResult.Options | Should -BeOfType "System.Net.NetworkInformation.PingOptions"
            }

            It 'calculates and stores the buffer size on the PingStatus result object' {
                $successfulResult.BufferSize | Should -Be 32
            }

            It "writes an error if the host cannot be found" {
                { Test-Connection "fakeHost" -Count 1 -Quiet -ErrorAction Stop } |
                    Should -Throw -ErrorId "TestConnectionException,Microsoft.PowerShell.Commands.TestConnectionCommand"

                # Error code = 11001 - Host not found.
                $ExpectedErrorCode = if ($isWindows) { 11001 } else { -131073 }
                $Error[0].Exception.InnerException.ErrorCode | Should -Be -$ExpectedErrorCode
            }
        }

        Context 'With -Count Parameter' {

            It "sends the requested number of pings to the targeted host" {
                param($Count)

                Test-Connection $targetName -Count $Count | Should -HaveCount $Count
            } -TestCases @(
                @{ Count = 1 }
                @{ Count = 2 }
                @{ Count = 5 }
            )
        }

        Context 'With -Quiet Switch' {

            It 'returns $true for reachable addresses' {
                Test-Connection $targetName -Count 1 -Quiet | Should -BeTrue
            }

            It 'returns $false for unreachable addresses' {
                Test-Connection $UnreachableAddress -Count 1 -Quiet | Should -BeFalse
            }
        }

        # In VSTS, address is 0.0.0.0
        It "Force IPv4 with implicit PingOptions" {
            $result = Test-Connection $hostName -Count 1 -IPv4

            $result[0].Address | Should -BeExactly $realAddress
            $result[0].Options.Ttl | Should -BeLessOrEqual 128
            if ($isWindows) {
                $result[0].Options.DontFragment | Should -BeFalse
            }
        }

        # In VSTS, address is 0.0.0.0
        It "Force IPv4 with explicit PingOptions" {
            $result1 = Test-Connection $hostName -Count 1 -IPv4 -MaxHops 10 -DontFragment

            # explicitly go to google dns. this test will pass even if the destination is unreachable
            # it's more about breaking out of the loop
            $result2 = Test-Connection 8.8.8.8 -Count 1 -IPv4 -MaxHops 1 -DontFragment

            $result1[0].Address | Should -BeExactly $realAddress
            $result1[0].Options.Ttl | Should -BeLessOrEqual 128

            if (!$isWindows) {
                $result1[0].Options.DontFragment | Should -BeTrue
                # Depending on the network configuration any of the following should be returned
                $result2[0].Status | Should -BeIn "TtlExpired", "TimedOut", "Success"
            }
            else {
                $result1[0].Options.DontFragment | Should -BeTrue
                # We expect 'TtlExpired' but if a router don't reply we get `TimedOut`
                # AzPipelines returns $null
                $result2[0].Status | Should -BeIn "TtlExpired", "TimedOut", $null
            }
        }

        It "Force IPv6" -Pending {
            $result = Test-Connection $targetName -Count 1 -IPv6

            $result[0].Address | Should -BeExactly $targetAddressIPv6
            # We should check Null not Empty!
            $result[0].Options | Should -Be $null
        }

        It "MaxHops Should -Be greater 0" {
            { Test-Connection $targetName -MaxHops 0 } |
                Should -Throw -ErrorId "System.ArgumentOutOfRangeException,Microsoft.PowerShell.Commands.TestConnectionCommand"
            { Test-Connection $targetName -MaxHops -1 } |
                Should -Throw -ErrorId "ParameterArgumentValidationError,Microsoft.PowerShell.Commands.TestConnectionCommand"
        }

        It "Count Should -Be greater 0" {
            { Test-Connection $targetName -Count 0 } | Should -Throw -ErrorId "ParameterArgumentValidationError,Microsoft.PowerShell.Commands.TestConnectionCommand"
            { Test-Connection $targetName -Count -1 } | Should -Throw -ErrorId "ParameterArgumentValidationError,Microsoft.PowerShell.Commands.TestConnectionCommand"
        }

        It "Delay Should -Be greater 0" {
            { Test-Connection $targetName -Delay 0 } |
                Should -Throw -ErrorId "ParameterArgumentValidationError,Microsoft.PowerShell.Commands.TestConnectionCommand"
            { Test-Connection $targetName -Delay -1 } |
                Should -Throw -ErrorId "ParameterArgumentValidationError,Microsoft.PowerShell.Commands.TestConnectionCommand"
        }

        It "Delay works" {
            $result1 = Measure-Command { Test-Connection localhost -Count 2 }
            $result2 = Measure-Command { Test-Connection localhost -Delay 4 -Count 2 }

            $result1.TotalSeconds | Should -BeGreaterThan 1
            $result1.TotalSeconds | Should -BeLessThan 3
            $result2.TotalSeconds | Should -BeGreaterThan 4
        }

        It "BufferSize Should -Be between 0 and 65500" {
            { Test-Connection $targetName -BufferSize 0 } | Should -Not -Throw
            { Test-Connection $targetName -BufferSize -1 } |
                Should -Throw -ErrorId "ParameterArgumentValidationError,Microsoft.PowerShell.Commands.TestConnectionCommand"
            { Test-Connection $targetName -BufferSize 65501 } |
                Should -Throw -ErrorId "ParameterArgumentValidationError,Microsoft.PowerShell.Commands.TestConnectionCommand"
        }

        It "BufferSize works" {
            $result = Test-Connection $targetName -Count 1 -BufferSize 2

            if ($isWindows) {
                $result.BufferSize | Should -Be 2
            }
        }

        It "ResolveDestination for address" {
            $result = Test-Connection $targetAddress -ResolveDestination -Count 1
            $resolvedName = [System.Net.DNS]::GetHostEntry($targetAddress).HostName

            $result.Destination | Should -BeExactly $resolvedName
            $result.Address | Should -BeExactly $targetAddress
        }

        It "ResolveDestination for name" {
            $result = Test-Connection $targetName -ResolveDestination -Count 1
            $resolvedName = [System.Net.DNS]::GetHostByName($targetName).HostName

            # PingReply in CoreFX doesn't return ScopeId in IPAddress (Bug?)
            # but GetHostAddresses() returns so remove it.
            $resolvedAddress = ([System.Net.DNS]::GetHostAddresses($resolvedName)[0] -split "%")[0]

            $result.Destination | Should -BeExactly $resolvedName
            $result.Address | Should -BeExactly $resolvedAddress
        }

        It "TimeOut works" {
            (Measure-Command { Test-Connection $UnreachableAddress -Count 1 -TimeOut 1 }).TotalSeconds |
                Should -BeLessThan 3
            (Measure-Command { Test-Connection $UnreachableAddress -Count 1 -TimeOut 4 }).TotalSeconds |
                Should -BeGreaterThan 3
        }

        It "Repeat works" {
            # By default we do 4 ping so for '-Repeat' we expect to get >4 results.
            # Also we should wait >4 seconds before check results but previous tests already did the pause.
            $pingResults = Receive-Job $jobContinues
            Remove-Job $jobContinues -Force

            $pingResults.Count | Should -BeGreaterThan 4
            $pingResults[0].Address | Should -BeExactly $targetAddress
            $pingResults.Status | Should -Contain "Success"
            if ($isWindows) {
                $pingResults.Where( { $_.Status -eq 'Success' }, 'Default', 1 ).BufferSize | Should -Be 32
            }
        }
    }

    # TODO: We skip the MTUSize tests on Unix because we expect 'PacketTooBig' but get 'TimeOut' internally from .Net Core
    Context "MTUSizeDetect" {
        It "MTUSizeDetect works" {
            $result = Test-Connection $hostName -MtuSize

            $result | Should -BeOfType "Microsoft.PowerShell.Commands.TestConnectionCommand+PingMtuStatus"
            $result.Destination | Should -BeExactly $hostName
            $result.Status | Should -BeExactly "Success"
            $result.MtuSize | Should -BeGreaterThan 0
        }

        It "Quiet works" {
            $result = Test-Connection $hostName -MtuSize -Quiet

            $result | Should -BeOfType "Int32"
            $result | Should -BeGreaterThan 0
        }
    }

    Context "TraceRoute" {
        It "TraceRoute works" {
            # real address is an ipv4 address, so force IPv4
            $result = Test-Connection $hostName -TraceRoute -IPv4

            $result[0] | Should -BeOfType "Microsoft.PowerShell.Commands.TestConnectionCommand+TraceStatus"
            $result[0].Source | Should -BeExactly $hostName
            $result[0].TargetAddress | Should -BeExactly $realAddress
            $result[0].Target | Should -BeExactly $hostName
            $result[0].Hop | Should -Be 1
            $result[0].HopAddress | Should -BeExactly $realAddress
            $result[0].Status | Should -BeExactly "Success"
            if (!$isWindows) {
                $result[0].Reply.Buffer.Count | Should -Match '^0$|^32$'
            }
            else {
                $result[0].Reply.Buffer.Count | Should -Be 32
            }
        }

        It "Quiet works" {
            $result = Test-Connection $hostName -TraceRoute -Quiet

            $result | Should -BeTrue
        }
    }
}

Describe "Connection" -Tag "CI", "RequireAdminOnWindows" {
    BeforeAll {
        # Ensure the local host listen on port 80
        $WebListener = Start-WebListener
        $UnreachableAddress = "10.11.12.13"
    }

    It "Test connection to local host port 80" {
        Test-Connection '127.0.0.1' -TcpPort $WebListener.HttpPort | Should -BeTrue
    }

    It "Test connection to unreachable host port 80" {
        Test-Connection $UnreachableAddress -TcpPort 80 -TimeOut 1 | Should -BeFalse
    }
}
