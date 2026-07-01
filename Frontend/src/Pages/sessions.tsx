import { Crown, Play } from 'lucide-react';
import ContentPage from '../Components/ContentPage';
import { useAppState } from '../Context/AppStateContext';
import ContentCard from '../Components/ContentCard';
import { includeInHighlight } from '../Models/types';
import { useAiHighlights } from '../Context/AiHighlightsContext';
import { useSettings } from '../Context/SettingsContext';
import { sendMessageToBackend } from '../Utils/MessageUtils';
import Button from '../Components/Button';

export default function Sessions() {
  const { content, recording } = useAppState();
  const { aiProgress } = useAiHighlights();
  const { enableAi } = useSettings();

  // Pre-render the progress card element
  const isRecordingFinishing = recording && recording.endTime !== null;
  const progressCardElement = isRecordingFinishing ? (
    <ContentCard key="recording-progress" type="Session" isLoading />
  ) : null;

  const processingFileNames = new Set(
    Object.values(aiProgress)
      .filter((progress) => progress.status === 'processing')
      .map((progress) => progress.content.fileName),
  );
  const eligibleSessions = content.filter(
    (item) =>
      item.type === 'Session' &&
      !processingFileNames.has(item.fileName) &&
      item.bookmarks.some((bookmark) => includeInHighlight(bookmark.type)),
  );

  const generateOldHighlights = () => {
    if (eligibleSessions.length === 0) return;
    sendMessageToBackend('CreateAiClipsForSessions', {
      FileNames: eligibleSessions.map((session) => session.fileName),
    });
  };

  return (
    <ContentPage
      contentType="Session"
      sectionId="sessions"
      title="Sessions"
      Icon={Play}
      progressItems={isRecordingFinishing ? { recording: true } : {}}
      isProgressVisible={isRecordingFinishing}
      progressCardElement={progressCardElement}
      headerActions={
        enableAi ? (
          <Button
            variant="primary"
            size="sm"
            className="no-animation h-8 gap-1"
            disabled={eligibleSessions.length === 0}
            onClick={generateOldHighlights}
            title={
              eligibleSessions.length > 0
                ? `Generate highlights and delete ${eligibleSessions.length} full session${eligibleSessions.length === 1 ? '' : 's'} after successful creation`
                : 'No old sessions with highlight moments'
            }
          >
            <Crown size={16} />
            Generate & Delete
          </Button>
        ) : null
      }
    />
  );
}
