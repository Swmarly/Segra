import { useEffect, useRef, useState } from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import { Download, FileText, Plane } from 'lucide-react';
import { GithubIcon } from '../icons/BrandIcons';
import DropdownSelect from '../DropdownSelect';
import { Settings as SettingsType } from '../../Models/types';
import { sendMessageToBackend } from '../../Utils/MessageUtils';
import Button from '../Button';
import { useAppState } from '../../Context/AppStateContext';
import { useUpdate } from '../../Context/UpdateContext';

interface AdvancedSectionProps {
  settings: SettingsType;
  updateSettings: (updates: Partial<SettingsType>) => void;
  openReleaseNotesModal: (version: string | null) => void;
}

export default function AdvancedSection({
  settings,
  updateSettings,
  openReleaseNotesModal,
}: AdvancedSectionProps) {
  const appState = useAppState();
  const { checkForUpdates, updateInfo } = useUpdate();
  const rowRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLSpanElement>(null);
  const [flyDistance, setFlyDistance] = useState(240);
  const isUpdateReady = updateInfo?.status === 'ready' || updateInfo?.status === 'downloaded';
  const isUpdating = appState.isCheckingForUpdates || updateInfo?.status === 'downloading';

  // Fly the plane from its spot to the right edge of the Airplane Mode row.
  useEffect(() => {
    const row = rowRef.current;
    if (!row) return;
    const measure = () => {
      const content = contentRef.current;
      if (!content) return;
      const distance = row.getBoundingClientRect().right - content.getBoundingClientRect().right;
      if (distance > 0) setFlyDistance(distance);
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(row);
    return () => observer.disconnect();
  }, []);

  return (
    <>
      <div className="bg-base-300 p-4 rounded-lg space-y-4 border border-custom">
        <div className="flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="primary"
              size="sm"
              className="w-40 bg-base-200 hover:bg-base-300"
              onClick={() => openReleaseNotesModal(null)}
            >
              <GithubIcon size={16} aria-hidden="true" />
              <span className="inline-block">View Release Notes</span>
            </Button>
            <Button
              variant={isUpdateReady ? 'success' : 'primary'}
              size="sm"
              className="w-40 bg-base-200 hover:bg-base-300"
              disabled={isUpdating}
              onClick={() =>
                isUpdateReady ? sendMessageToBackend('ApplyUpdate') : checkForUpdates()
              }
            >
              {isUpdating ? (
                <span className="loading loading-spinner loading-xs" aria-hidden="true" />
              ) : (
                <Download size={16} aria-hidden="true" />
              )}
              <span className="inline-block">
                {isUpdating ? 'Checking...' : isUpdateReady ? 'Install Update' : 'Check for Update'}
              </span>
            </Button>
          </div>
        </div>

        {/* OBS Version Selection */}
        <div className="flex items-center justify-between">
          <div className="flex flex-col">
            <div className="mb-1">
              <span className="text-base-content">OBS Version</span>
            </div>
            <div className="w-40">
              <DropdownSelect
                size="sm"
                items={[
                  { value: '', label: 'Automatic' },
                  ...[...appState.availableOBSVersions]
                    .sort((a, b) => {
                      return b.version.localeCompare(a.version, undefined, { numeric: true });
                    })
                    .map((v) => ({
                      value: v.version,
                      label: `${v.version}${v.isBeta ? ' (Beta)' : ''}`,
                    })),
                ]}
                value={settings.selectedOBSVersion || ''}
                onChange={(val) => updateSettings({ selectedOBSVersion: val || null })}
              />
            </div>
          </div>
        </div>

        {/* Airplane Mode */}
        <div ref={rowRef} className="pt-4 border-t border-custom">
          <label className="flex items-center gap-2">
            <input
              type="checkbox"
              name="airplaneMode"
              checked={settings.airplaneMode}
              onChange={(e) => updateSettings({ airplaneMode: e.target.checked })}
              className="toggle toggle-primary toggle-sm"
            />
            <span ref={contentRef} className="inline-flex items-center gap-1.5 cursor-pointer">
              Airplane Mode
              <AnimatePresence initial={false}>
                {settings.airplaneMode && (
                  <motion.span
                    className="inline-flex"
                    initial={{ opacity: 0, x: -8, rotate: 45 }}
                    animate={{ opacity: 1, x: 0, rotate: 45 }}
                    exit={{
                      opacity: 0,
                      x: flyDistance,
                      rotate: 45,
                      transition: {
                        x: { duration: 1.9, ease: [0.2, 0, 0.8, 0] },
                        opacity: { duration: 0.9, ease: 'easeIn', delay: 1.0 },
                      },
                    }}
                    transition={{ duration: 0.35, ease: 'easeOut' }}
                  >
                    <Plane size={16} />
                  </motion.span>
                )}
              </AnimatePresence>
            </span>
          </label>
        </div>
      </div>

      {/* Version */}
      <div className="text-center mt-4 text-sm text-gray-500">
        <div className="flex flex-col items-center gap-2">
          <Button
            variant="primary"
            size="sm"
            onClick={() => sendMessageToBackend('OpenLogsLocation')}
          >
            <FileText className="w-4 h-4 shrink-0" aria-hidden="true" />
            <span className="leading-none">View Logs</span>
          </Button>
          <div>
            Segra{' '}
            {__APP_VERSION__ === 'Developer Preview' ? __APP_VERSION__ : 'v' + __APP_VERSION__}
          </div>
        </div>
      </div>
    </>
  );
}
