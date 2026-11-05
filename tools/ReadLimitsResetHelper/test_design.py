import unittest
from pathlib import Path
from threeway import cases
from jit_summary import selected, allows_empty
from verify_production import transform


class DesignTests(unittest.TestCase):
    def test_inventory(self):
        self.assertEqual(len(cases('legacy')), 10)
        self.assertEqual(len(cases('selected')), 8)
        for suite in ('legacy', 'selected'):
            self.assertEqual(len(cases(suite)), len(set(cases(suite))))

    def test_reference_and_clean_controls(self):
        self.assertIn(('aggregate', 'References32.empty'), cases('legacy'))
        self.assertIn(('aggregate', 'Int32.empty'), cases('legacy'))
        self.assertIn(('component', 'DeserializeInt'), cases('selected'))
        self.assertIn(('copy', 'CopyReuse.raw.pinned0.65536'), cases('selected'))

    def test_reset_helper_selected(self):
        self.assertTrue(selected({'method': 'SharpPack.SharpPackReaderOptionalState:ResetReferences() (Tier1)'}, 'SharpPack.Core'))
        self.assertTrue(allows_empty('SharpPack.Core', 'CopyReuse.raw.pinned0.65536'))
        self.assertFalse(allows_empty('SharpPack.Core', 'DeserializeInt'))

    def test_balanced_triples(self):
        orders = ('ABC', 'CBA', 'BCA', 'ACB', 'CAB', 'BAC', 'ABC', 'CBA')
        for side in 'ABC':
            self.assertEqual(sum(order.count(side) for order in orders), 8)
        for a, b in (('A', 'B'), ('A', 'C'), ('B', 'C')):
            self.assertEqual(sum(order.index(a) < order.index(b) for order in orders), 4)

    def test_transformation_preserves_original_blocks(self):
        original = ('prefix public sealed class State\n    public void Reset()\n    {\n'
                    '        OriginalTableCleanup();\n\n'
                    '        SerializerContext = null;\n        OriginalStores();\n    }\n\n'
                    '    void IDisposable.Dispose()\n    { tail }\n')
        after = transform(original)
        self.assertEqual(after.count('OriginalTableCleanup();'), 1)
        self.assertEqual(after.count('OriginalStores();'), 1)
        self.assertIn('sequentialReferences is not null || sparseReferences is not null', after)
        self.assertIn('MethodImplOptions.NoInlining', after)
        self.assertTrue(after.endswith('    void IDisposable.Dispose()\n    { tail }\n'))


if __name__ == '__main__': unittest.main()
