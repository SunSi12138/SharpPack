import unittest
from jit_summary import blocks, selected


class JitSummaryTests(unittest.TestCase):
    def test_x64_counts_and_addresses(self):
        rows = blocks('''; Assembly listing for method Program:ReadInt() (Tier1)
G_M000_IG01:
       push rbx
       call [SharpPack.SharpPackReaderOptionalState:Init():this]
       call 0x1234ABCDE
       ret
; Total bytes of code 48
''')
        self.assertEqual(48, rows[0]['codeBytes'])
        self.assertEqual(4, rows[0]['instructionCount'])
        self.assertEqual(1, rows[0]['opcodes']['ret'])
        self.assertNotIn('0x1234ABCDE', str(rows))
        self.assertEqual('Tier1', rows[0]['tier'])

    def test_arm_and_tail_call(self):
        rows = blocks('''; Assembly listing for method SharpPack.SharpPackSerializer:Deserialize[int]() (FullOpts)
       stp fp, lr, [sp, #-32]!
       bl SharpPack.SharpPackReaderOptionalState:Init()
       b CORINFO_HELP_THROW
; Total bytes of code 12
''')
        self.assertEqual(3, rows[0]['instructionCount'])
        self.assertEqual(2, sum(rows[0]['calls'].values()))
        self.assertTrue(selected(rows[0], 'SharpPack.Core'))

    def test_incomplete_fails(self):
        with self.assertRaises(RuntimeError): blocks('; Assembly listing for method A:B() (Tier0)\n mov rax, rbx')

    def test_unrelated_core_excluded(self):
        self.assertFalse(selected({'method': 'Unrelated:Init() (Tier1)'}, 'SharpPack.Core'))


if __name__ == '__main__': unittest.main()
