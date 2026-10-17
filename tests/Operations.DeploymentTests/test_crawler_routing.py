"""Keep crawler and wishlist regular-expression captures in separate namespaces."""
from pathlib import Path
import unittest


class CrawlerRoutingTests(unittest.TestCase):
    def test_crawler_matcher_does_not_overwrite_wishlist_identifier(self):
        # Arrange
        path = Path(__file__).resolve().parents[2] / 'deployments/frontend/frontend.caddy'

        # Act
        configuration = path.read_text(encoding='utf-8')

        # Assert
        self.assertIn('path_regexp sharePreview ', configuration)
        self.assertIn('header_regexp shareCrawler User-Agent ', configuration)
        self.assertNotIn('header_regexp User-Agent ', configuration)
        self.assertIn('rewrite * /api/v1/shared-wishlists/{re.sharePreview.1}/preview', configuration)
