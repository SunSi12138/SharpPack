import unittest
from jit_visibility import observe, prefix_versions


def listing(tier='Tier1', footer=True):
    result = ('; Assembly listing for method Program:g__RunBatch(System.Action,int) (' + tier + ')\n'
              '       mov eax, 1\n       ret\n')
    if footer: result += '; Total bytes of code 6\n'
    return result.encode()


class VisibilityTests(unittest.TestCase):
    def test_complete_before(self):
        raw = listing()
        result = observe(raw, len(raw), len(raw))
        self.assertEqual(len(result['before']), 1)
        self.assertFalse(result['between'])
        self.assertFalse(result['afterSnapshot'])

    def test_late_flush_not_backdated(self):
        raw = listing()
        result = observe(raw, 0, 0)
        self.assertFalse(result['before'])
        self.assertEqual(len(result['afterSnapshot']), 1)

    def test_partial_before_complete_between(self):
        raw = listing()
        result = observe(raw, len(raw) - 4, len(raw))
        self.assertFalse(result['before'])
        self.assertEqual(result['incompleteBefore'], 1)
        self.assertEqual(len(result['between']), 1)

    def test_raw_byte_offsets(self):
        prefix = '中文\n'.encode()
        raw = prefix + listing()
        self.assertFalse(prefix_versions(raw, len('中文\n') + len(listing()))['versions'])
        self.assertEqual(len(prefix_versions(raw, len(raw))['versions']), 1)

    def test_osr_and_multiple_versions(self):
        first = listing('Tier0')
        raw = first + listing('Tier1-OSR')
        result = observe(raw, len(first), len(raw))
        self.assertEqual(len(result['before']), 1)
        self.assertEqual(len(result['between']), 1)
        self.assertTrue(result['summaries'][result['between'][0]]['osr'])

    def test_missing_or_invalid_is_unknown(self):
        for raw, before, after in [(None, -1, -1), (listing(), -1, 5), (listing(), 10, 5), (listing(), 0, 10000)]:
            self.assertEqual(observe(raw, before, after)['status'], 'unknown')

    def test_unrelated_method_does_not_qualify(self):
        raw = listing().replace(b'g__RunBatch', b'OtherMethod')
        self.assertFalse(observe(raw, len(raw), len(raw))['before'])

    def test_incomplete_exit_keeps_complete_versions_but_is_unknown(self):
        first = listing('Tier0')
        raw = first + listing('Tier1', footer=False)
        result = observe(raw, len(first), len(raw))
        self.assertEqual(result['status'], 'unknown')
        self.assertEqual(len(result['before']), 1)
        self.assertEqual(result['incompleteExit'], 1)

    def test_missing_tier_is_unknown(self):
        raw = listing().replace(b' (Tier1)', b'')
        result = observe(raw, len(raw), len(raw))
        self.assertEqual(result['status'], 'unknown')
        self.assertEqual(len(result['before']), 1)


if __name__ == '__main__': unittest.main()
